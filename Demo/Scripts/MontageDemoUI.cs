using UnityEngine;

namespace Cwcbb.Tools.CwcMontage.Demo
{
    /// <summary>
    /// 蒙太奇演示运行时交互控制面板。
    /// 基于轻量级 IMGUI (OnGUI) 构建，零外部 UI 资源依赖，跨渲染管线通用。
    /// 提供实时播放监控（进度、分段、权重）、动作切换、倍速调节、分段跳转与时钟自适应缩放测试。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MontageDemoController))]
    public class MontageDemoUI : MonoBehaviour
    {
        #region 常量定义

        private const float PANEL_WIDTH = 340f;
        private const float PANEL_PADDING = 10f;

        #endregion

        #region Inspector 字段

        [Header("面板显示设置")]
        [Tooltip("面板在屏幕上的左上角偏移")]
        [SerializeField] private Vector2 _panelOffset = new(15f, 15f);

        [Tooltip("是否允许一键折叠面板")]
        [SerializeField] private bool _isExpanded = true;

        #endregion

        #region 私有字段

        private MontageDemoController _controller;
        private GUIStyle _boxStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _boldLabelStyle;
        private GUIStyle _statusPlayingStyle;
        private GUIStyle _statusPausedStyle;
        private GUIStyle _statusIdleStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _activeButtonStyle;
        private bool _isStylesInitialized;

        #endregion

        #region 属性

        /// <summary>
        /// 面板当前是否展开显示。
        /// </summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set => _isExpanded = value;
        }

        #endregion

        #region Unity 生命周期

        private void Awake()
        {
            _controller = GetComponent<MontageDemoController>();
            if (_controller == null)
            {
                Debug.LogError($"[MontageDemoUI] 在物体 '{gameObject.name}' 上未找到 MontageDemoController 组件！", this);
            }
        }

        private void OnGUI()
        {
            if (_controller == null) return;

            InitStylesIfNeeded();

            float screenHeight = Screen.height;
            float maxPanelHeight = screenHeight - _panelOffset.y * 2;

            GUILayout.BeginArea(new Rect(_panelOffset.x, _panelOffset.y, PANEL_WIDTH, maxPanelHeight));
            GUILayout.BeginVertical(_boxStyle);

            // 1. 顶部标题栏与折叠切换
            DrawHeader();

            if (_isExpanded)
            {
                GUILayout.Space(6f);

                // 2. 实时状态监控
                DrawMonitorSection();

                GUILayout.Space(8f);

                // 3. 蒙太奇动作切换
                DrawMontageSelector();

                GUILayout.Space(8f);

                // 4. 播放控制与倍速
                DrawPlaybackControls();

                GUILayout.Space(8f);

                // 5. 核心特性：分段与时钟缩放测试
                DrawSectionControls();

                GUILayout.Space(8f);

                // 6. 快捷键指南
                DrawHelpSection();
            }

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        #endregion

        #region 私有绘制方法

        private void InitStylesIfNeeded()
        {
            if (_isStylesInitialized) return;

            // 基础面板样式 (深色半透明背景)
            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(12, 12, 10, 10)
            };

            // 标题样式
            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.9f, 0.95f, 1.0f) }
            };

            // 常规标签
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.8f, 0.8f, 0.85f) }
            };

            // 强调标签
            _boldLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            // 状态标签样式
            _statusPlayingStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.3f, 0.9f, 0.4f) }
            };

            _statusPausedStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1.0f, 0.8f, 0.2f) }
            };

            _statusIdleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.6f, 0.6f, 0.65f) }
            };

            // 普通按钮
            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fixedHeight = 26
            };

            // 高亮激活按钮
            _activeButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                fixedHeight = 26,
                normal = { textColor = new Color(0.4f, 0.85f, 1.0f) }
            };

            _isStylesInitialized = true;
        }

        private void DrawHeader()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("CwcMontage Demo", _headerStyle);
            GUILayout.FlexibleSpace();

            string toggleText = _isExpanded ? "Collapse" : "Expand";
            if (GUILayout.Button(toggleText, GUILayout.Width(70), GUILayout.Height(22)))
            {
                _isExpanded = !_isExpanded;
            }
            GUILayout.EndHorizontal();
        }

        private void DrawMonitorSection()
        {
            GUILayout.Label("--- Live Monitor ---", _boldLabelStyle);

            var handle = _controller.ActiveHandle;
            bool isHandleValid = handle.IsValid;
            bool isPlaying = isHandleValid && handle.IsPlaying;
            bool isPaused = isHandleValid && handle.IsPaused;

            // 状态标签
            GUILayout.BeginHorizontal();
            GUILayout.Label("Status:", _labelStyle, GUILayout.Width(70));
            if (isPaused)
            {
                GUILayout.Label("[PAUSED]", _statusPausedStyle);
            }
            else if (isPlaying)
            {
                GUILayout.Label("[PLAYING]", _statusPlayingStyle);
            }
            else
            {
                GUILayout.Label("[IDLE]", _statusIdleStyle);
            }
            GUILayout.EndHorizontal();

            // 当前蒙太奇名字
            string montageName = "None";
            if (isHandleValid && handle.SourceAsset != null)
            {
                montageName = handle.SourceAsset.name;
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label("Montage:", _labelStyle, GUILayout.Width(70));
            GUILayout.Label(montageName, _boldLabelStyle);
            GUILayout.EndHorizontal();

            // 时间与进度
            float elapsed = isHandleValid ? handle.ElapsedTime : 0f;
            float total = isHandleValid ? handle.TotalDuration : 0f;
            float progress = isHandleValid ? handle.NormalizedTime : 0f;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Time:", _labelStyle, GUILayout.Width(70));
            GUILayout.Label($"{elapsed:F2}s / {total:F2}s ({(progress * 100f):F0}%)", _labelStyle);
            GUILayout.EndHorizontal();

            // 进度条
            Rect progressRect = GUILayoutUtility.GetRect(PANEL_WIDTH - 24, 8);
            GUI.Box(progressRect, GUIContent.none);
            Rect fillRect = new(progressRect.x, progressRect.y, progressRect.width * progress, progressRect.height);
            GUI.DrawTexture(fillRect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(0.2f, 0.7f, 1.0f), 0, 0);

            // 分段信息
            int currentSection = isHandleValid ? handle.CurrentSectionIndex : -1;
            int sectionCount = isHandleValid ? handle.SectionCount : 0;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Section:", _labelStyle, GUILayout.Width(70));
            GUILayout.Label($"Index: {currentSection} / Count: {sectionCount}", _labelStyle);
            GUILayout.EndHorizontal();

            // 混音器权重与图层
            float weight = isHandleValid ? handle.CurrentWeight : 0f;
            int layer = isHandleValid ? handle.LayerIndex : 0;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Mixer:", _labelStyle, GUILayout.Width(70));
            GUILayout.Label($"Layer: {layer}, Weight: {weight:F2}, Slot: {handle.SlotIndex}", _labelStyle);
            GUILayout.EndHorizontal();
        }

        private void DrawMontageSelector()
        {
            GUILayout.Label("--- Actions (CrossFade) ---", _boldLabelStyle);

            var montages = _controller.DemoMontages;
            if (montages == null || montages.Count == 0)
            {
                GUILayout.Label("No montages assigned in controller.", _labelStyle);
                return;
            }

            int currentIndex = _controller.CurrentMontageIndex;
            for (int i = 0; i < montages.Count; i++)
            {
                var m = montages[i];
                string name = m != null ? m.name : $"(Empty {i})";
                string btnLabel = $"[{i + 1}] Play {name}";

                bool isCurrent = (i == currentIndex && _controller.ActiveHandle.IsValid && _controller.ActiveHandle.IsPlaying);
                var styleToUse = isCurrent ? _activeButtonStyle : _buttonStyle;

                if (GUILayout.Button(btnLabel, styleToUse))
                {
                    _controller.PlayMontage(i);
                }
            }
        }

        private void DrawPlaybackControls()
        {
            GUILayout.Label("--- Playback Controls ---", _boldLabelStyle);

            var handle = _controller.ActiveHandle;
            bool isPaused = handle.IsValid && handle.IsPaused;

            GUILayout.BeginHorizontal();
            string pauseBtnText = isPaused ? "Resume (Space)" : "Pause (Space)";
            if (GUILayout.Button(pauseBtnText, _buttonStyle))
            {
                _controller.TogglePause();
            }

            if (GUILayout.Button("Stop (S)", _buttonStyle))
            {
                _controller.StopCurrent();
            }
            GUILayout.EndHorizontal();

            // 播放速率选择
            GUILayout.BeginHorizontal();
            GUILayout.Label("Speed:", _labelStyle, GUILayout.Width(50));
            float[] speeds = { 0.5f, 1.0f, 1.5f, 2.0f };
            for (int s = 0; s < speeds.Length; s++)
            {
                float sp = speeds[s];
                bool isSelected = Mathf.Abs(_controller.CurrentSpeed - sp) < 0.01f;
                var style = isSelected ? _activeButtonStyle : _buttonStyle;

                if (GUILayout.Button($"{sp:F1}x", style, GUILayout.Width(60)))
                {
                    _controller.SetSpeed(sp);
                }
            }
            GUILayout.EndHorizontal();
        }

        private void DrawSectionControls()
        {
            GUILayout.Label("--- Section & Clock Warping ---", _boldLabelStyle);

            // 分段跳转按钮
            if (GUILayout.Button("Jump to Next Section (Tab)", _buttonStyle))
            {
                _controller.JumpToNextSection();
            }

            // 自适应时钟缩放测试
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Fast (0.2s)", _buttonStyle))
            {
                _controller.SyncCurrentSectionDuration(0.2f);
            }
            if (GUILayout.Button("Normal (0.5s)", _buttonStyle))
            {
                _controller.SyncCurrentSectionDuration(0.5f);
            }
            if (GUILayout.Button("Slow (1.0s)", _buttonStyle))
            {
                _controller.SyncCurrentSectionDuration(1.0f);
            }
            GUILayout.EndHorizontal();
        }

        private void DrawHelpSection()
        {
            GUILayout.Label("--- Keyboard Shortcuts ---", _boldLabelStyle);
            GUILayout.Label("• 1-9: Play specific montage", _labelStyle);
            GUILayout.Label("• Space: Pause / Resume", _labelStyle);
            GUILayout.Label("• S: Smooth Stop", _labelStyle);
            GUILayout.Label("• Tab: Jump to next section", _labelStyle);
            GUILayout.Label("• Q / E / R: Speed 2.0x / 0.5x / 1.0x", _labelStyle);
        }

        #endregion
    }
}
