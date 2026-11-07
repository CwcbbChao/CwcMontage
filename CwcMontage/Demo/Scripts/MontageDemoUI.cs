using UnityEngine;

namespace Cwcbb.Tools.CwcMontage.Demo
{
    /// <summary>
    /// Runtime interactive control panel for the montage demo.
    /// Built on lightweight IMGUI (OnGUI) with no external UI asset dependencies and works across render pipelines.
    /// Provides live playback monitoring (progress, section, weight), action switching, speed adjustment, section jumping, and adaptive clock scaling tests.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MontageDemoController))]
    public class MontageDemoUI : MonoBehaviour
    {
        #region Constants

        private const float PANEL_WIDTH = 340f;
        private const float PANEL_PADDING = 10f;

        #endregion

        #region Serialized Fields

        [Header("Panel Display Settings")]
        [Tooltip("Top-left offset of the panel on screen.")]
        [SerializeField] private Vector2 _panelOffset = new(15f, 15f);

        [Tooltip("Whether the panel can be collapsed with one click.")]
        [SerializeField] private bool _isExpanded = true;

        #endregion

        #region Private Fields

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

        #region Properties

        /// <summary>
        /// Whether the panel is currently expanded.
        /// </summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set => _isExpanded = value;
        }

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            _controller = GetComponent<MontageDemoController>();
            if (_controller == null)
            {
                Debug.LogError($"[MontageDemoUI] MontageDemoController component not found on object '{gameObject.name}'!", this);
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

            // 1. Top title bar and collapse toggle
            DrawHeader();

            if (_isExpanded)
            {
                GUILayout.Space(6f);

                // 2. Live status monitoring
                DrawMonitorSection();

                GUILayout.Space(8f);

                // 3. Montage action switching
                DrawMontageSelector();

                GUILayout.Space(8f);

                // 4. Playback controls and speed
                DrawPlaybackControls();

                GUILayout.Space(8f);

                // 5. Core feature: section and clock scaling tests
                DrawSectionControls();

                GUILayout.Space(8f);

                // 6. Keyboard shortcut guide
                DrawHelpSection();
            }

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        #endregion

        #region Private GUI Rendering Methods

        private void InitStylesIfNeeded()
        {
            if (_isStylesInitialized) return;

            // Base panel style (dark translucent background)
            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(12, 12, 10, 10)
            };

            // Header style
            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.9f, 0.95f, 1.0f) }
            };

            // Regular label
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.8f, 0.8f, 0.85f) }
            };

            // Emphasis label
            _boldLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            // Status label style
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

            // Normal button
            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fixedHeight = 26
            };

            // Highlighted active button
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

            // Status label
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

            // Current montage name
            string montageName = "None";
            if (isHandleValid && handle.SourceAsset != null)
            {
                montageName = handle.SourceAsset.name;
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label("Montage:", _labelStyle, GUILayout.Width(70));
            GUILayout.Label(montageName, _boldLabelStyle);
            GUILayout.EndHorizontal();

            // Time and progress
            float elapsed = isHandleValid ? handle.ElapsedTime : 0f;
            float total = isHandleValid ? handle.TotalDuration : 0f;
            float progress = isHandleValid ? handle.NormalizedTime : 0f;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Time:", _labelStyle, GUILayout.Width(70));
            GUILayout.Label($"{elapsed:F2}s / {total:F2}s ({(progress * 100f):F0}%)", _labelStyle);
            GUILayout.EndHorizontal();

            // Progress bar
            Rect progressRect = GUILayoutUtility.GetRect(PANEL_WIDTH - 24, 8);
            GUI.Box(progressRect, GUIContent.none);
            Rect fillRect = new(progressRect.x, progressRect.y, progressRect.width * progress, progressRect.height);
            GUI.DrawTexture(fillRect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(0.2f, 0.7f, 1.0f), 0, 0);

            // Section info
            int currentSection = isHandleValid ? handle.CurrentSectionIndex : -1;
            int sectionCount = isHandleValid ? handle.SectionCount : 0;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Section:", _labelStyle, GUILayout.Width(70));
            GUILayout.Label($"Index: {currentSection} / Count: {sectionCount}", _labelStyle);
            GUILayout.EndHorizontal();

            // Mixer weight and layer
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

            // Playback rate selection
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

            // Section jump button
            if (GUILayout.Button("Jump to Next Section (Tab)", _buttonStyle))
            {
                _controller.JumpToNextSection();
            }

            // Adaptive clock scaling test
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
