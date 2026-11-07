using System.Collections.Generic;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage.Demo
{
    /// <summary>
    /// Montage runtime demo controller component.
    /// Works with MontageController to respond to UI or keyboard shortcut input, driving montage playback, pause, speed adjustment, section jumps, and adaptive clock scaling.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MontageController))]
    public class MontageDemoController : MonoBehaviour
    {
        #region Constants

        private const float DEFAULT_BLEND_OUT_TIME = 0.2f;

        #endregion

        #region Serialized Fields

        [Header("Demo Montage Assets")]
        [Tooltip("Montage configuration assets available for demo switching")]
        [SerializeField] private List<MontageSequenceSO> _demoMontages = new();

        [Header("Playback Settings")]
        [Tooltip("Whether to automatically play the first montage when the scene starts")]
        [SerializeField] private bool _playOnStart = true;

        [Tooltip("Whether to enable keyboard shortcut controls (number keys 1-9 to switch, Space to pause, S to stop, Tab to jump sections)")]
        [SerializeField] private bool _enableKeyboardShortcuts = true;

        #endregion

        #region Private Fields

        private MontageController _controller;
        private MontageHandle _activeHandle;
        private int _currentMontageIndex = -1;
        private float _currentSpeed = 1.0f;

        #endregion

        #region Properties

        /// <summary>
        /// The handle of the montage currently playing or being controlled.
        /// </summary>
        public MontageHandle ActiveHandle => _activeHandle;

        /// <summary>
        /// Read-only view of the demo montage asset list.
        /// </summary>
        public IReadOnlyList<MontageSequenceSO> DemoMontages => _demoMontages;

        /// <summary>
        /// Index of the currently playing montage. Returns -1 if none.
        /// </summary>
        public int CurrentMontageIndex => _currentMontageIndex;

        /// <summary>
        /// The currently configured playback rate.
        /// </summary>
        public float CurrentSpeed => _currentSpeed;

        /// <summary>
        /// The associated driver controller component.
        /// </summary>
        public MontageController Controller => _controller;

        /// <summary>
        /// Backward-compatibility alias for Controller.
        /// </summary>
        [System.Obsolete("Use Controller instead.")]
        public MontageController Coordinator => _controller;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            _controller = GetComponent<MontageController>();
            if (_controller == null)
            {
                Debug.LogError($"[MontageDemoController] MontageController component not found on object '{gameObject.name}'!", this);
            }
        }

        private void Start()
        {
            if (_playOnStart && _demoMontages.Count > 0 && _demoMontages[0] != null)
            {
                PlayMontage(0);
            }
        }

        private void Update()
        {
            if (_enableKeyboardShortcuts)
            {
                HandleKeyboardInput();
            }
        }

        #endregion

        #region Public Control Methods

        /// <summary>
        /// Plays the montage asset at the specified index.
        /// </summary>
        /// <param name="index">Index of the montage in the list</param>
        /// <param name="customBlendInTime">Custom smooth blend-in duration (seconds)</param>
        public void PlayMontage(int index, float? customBlendInTime = null)
        {
            if (_controller == null)
            {
                Debug.LogWarning($"[MontageDemoController] Controller is not initialized, cannot play!", this);
                return;
            }

            if (index < 0 || index >= _demoMontages.Count)
            {
                Debug.LogWarning($"[MontageDemoController] Index {index} is out of valid range (0 ~ {_demoMontages.Count - 1})!", this);
                return;
            }

            var targetMontage = _demoMontages[index];
            if (targetMontage == null)
            {
                Debug.LogWarning($"[MontageDemoController] The montage asset at index {index} is null!", this);
                return;
            }

            _currentMontageIndex = index;
            _activeHandle = _controller.Play(targetMontage, customBlendInTime);

            if (_activeHandle.IsValid && Mathf.Abs(_currentSpeed - 1.0f) > 0.001f)
            {
                _activeHandle.SetPlaybackRate(_currentSpeed);
            }
        }

        /// <summary>
        /// Toggles the pause/resume state of the current montage.
        /// </summary>
        public void TogglePause()
        {
            if (!_activeHandle.IsValid) return;

            _activeHandle.SetPaused(!_activeHandle.IsPaused);
        }

        /// <summary>
        /// Stops the currently playing montage.
        /// </summary>
        /// <param name="blendOutTime">Fade-out transition duration</param>
        public void StopCurrent(float blendOutTime = DEFAULT_BLEND_OUT_TIME)
        {
            if (_activeHandle.IsValid)
            {
                _activeHandle.Stop(blendOutTime);
            }
            _currentMontageIndex = -1;
        }

        /// <summary>
        /// Sets the playback rate multiplier.
        /// </summary>
        /// <param name="speed">Target playback rate</param>
        public void SetSpeed(float speed)
        {
            _currentSpeed = Mathf.Max(0.01f, speed);
            if (_activeHandle.IsValid)
            {
                _activeHandle.SetPlaybackRate(_currentSpeed);
            }
        }

        /// <summary>
        /// Immediately jumps to the start time of the specified physical section.
        /// </summary>
        /// <param name="sectionIndex">Physical section index</param>
        public void JumpToSection(int sectionIndex)
        {
            if (!_activeHandle.IsValid) return;

            if (sectionIndex >= 0 && sectionIndex < _activeHandle.SectionCount)
            {
                _activeHandle.JumpToSection(sectionIndex);
            }
            else
            {
                Debug.LogWarning($"[MontageDemoController] Section index {sectionIndex} is out of range (0 ~ {_activeHandle.SectionCount - 1})!", this);
            }
        }

        /// <summary>
        /// Cycles to the next physical section.
        /// </summary>
        public void JumpToNextSection()
        {
            if (!_activeHandle.IsValid || _activeHandle.SectionCount <= 0) return;

            int current = _activeHandle.CurrentSectionIndex;
            int next = (current + 1) % _activeHandle.SectionCount;
            _activeHandle.JumpToSection(next);
        }

        /// <summary>
        /// Dynamically adjusts the target physical duration of the current physical section (adaptive clock scaling).
        /// Resolves the conflict between the original animation length and the duration required by the designers.
        /// </summary>
        /// <param name="targetDuration">Target actual playback duration (seconds)</param>
        public void SyncCurrentSectionDuration(float targetDuration)
        {
            if (!_activeHandle.IsValid) return;

            int currentSection = _activeHandle.CurrentSectionIndex;
            if (currentSection >= 0)
            {
                _activeHandle.SyncSectionDuration(currentSection, targetDuration);
            }
        }

        #endregion

        #region Private Helper Methods

        private void HandleKeyboardInput()
        {
            // Number keys 1-9 switch montages
            for (int i = 0; i < 9 && i < _demoMontages.Count; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                {
                    PlayMontage(i);
                    return;
                }
            }

            // Space key: pause / resume
            if (Input.GetKeyDown(KeyCode.Space))
            {
                TogglePause();
                return;
            }

            // S key: smooth stop
            if (Input.GetKeyDown(KeyCode.S))
            {
                StopCurrent();
                return;
            }

            // Tab key: jump to the next section
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                JumpToNextSection();
                return;
            }

            // Q key: speed up (2.0x), E key: slow down (0.5x), R key: reset to normal (1.0x)
            if (Input.GetKeyDown(KeyCode.Q))
            {
                SetSpeed(2.0f);
            }
            else if (Input.GetKeyDown(KeyCode.E))
            {
                SetSpeed(0.5f);
            }
            else if (Input.GetKeyDown(KeyCode.R))
            {
                SetSpeed(1.0f);
            }
        }

        #endregion
    }
}
