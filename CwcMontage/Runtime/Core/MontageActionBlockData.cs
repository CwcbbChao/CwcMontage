using System;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// Serialized data container for a single action block on a timeline track.
    /// </summary>
    [Serializable]
    public class MontageActionBlockData
    {
        #region Serialized Fields

        [Tooltip("Start time on the montage timeline in seconds.")]
        [SerializeField] private float _startTime;

        [Tooltip("End time on the montage timeline in seconds.")]
        [SerializeField] private float _endTime;

        [Tooltip("Start frame index.")]
        [SerializeField] private int _startFrame;

        [Tooltip("End frame index.")]
        [SerializeField] private int _endFrame;

        [Tooltip("Polymorphic action block logic instance.")]
        [SerializeReference] private MontageActionBlockBase _action;

        #endregion

        #region Properties

        /// <summary>
        /// Start time on the timeline in seconds.
        /// </summary>
        public float StartTime
        {
            get => _startTime;
            set => _startTime = value;
        }

        /// <summary>
        /// End time on the timeline in seconds.
        /// </summary>
        public float EndTime
        {
            get => _endTime;
            set => _endTime = value;
        }

        /// <summary>
        /// Start frame index.
        /// </summary>
        public int StartFrame
        {
            get => _startFrame;
            set => _startFrame = value;
        }

        /// <summary>
        /// End frame index.
        /// </summary>
        public int EndFrame
        {
            get => _endFrame;
            set => _endFrame = value;
        }

        /// <summary>
        /// Block duration in seconds.
        /// </summary>
        public float Duration => Mathf.Max(0f, _endTime - _startTime);

        /// <summary>
        /// Polymorphic action logic instance.
        /// </summary>
        public MontageActionBlockBase Action
        {
            get => _action;
            set => _action = value;
        }

        /// <summary>
        /// Whether this action block is enabled.
        /// </summary>
        public bool IsEnabled => _action == null || _action.IsEnabled;

        /// <summary>
        /// Whether this action block is disabled (backward compatibility).
        /// </summary>
        public bool IsDisabled => _action != null && !_action.IsEnabled;

        /// <summary>
        /// Reference to original source asset data for runtime clones.
        /// </summary>
        [NonSerialized] public MontageActionBlockData SourceData;

        #endregion

        #region Constructors

        public MontageActionBlockData()
        {
        }

        public MontageActionBlockData(float startTime, float endTime, int startFrame, int endFrame, MontageActionBlockBase action)
        {
            _startTime = startTime;
            _endTime = Mathf.Max(endTime, startTime + 0.0001f);
            _startFrame = startFrame;
            _endFrame = Mathf.Max(endFrame, startFrame + 1);
            _action = action;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Validates and normalizes start/end frames and times, enforcing a minimum duration of 1 frame.
        /// </summary>
        /// <param name="frameRate">Animation baseline frame rate (FPS).</param>
        public void EnsureValid(float frameRate)
        {
            float safeFps = frameRate > 0.01f ? frameRate : 30.0f;
            float minDuration = 1.0f / safeFps;

            if (_startFrame < 0)
            {
                _startFrame = 0;
            }

            if (_endFrame <= _startFrame)
            {
                _endFrame = _startFrame + 1;
            }

            _startTime = Mathf.Max(0.0f, _startTime);
            if (_endTime < _startTime + minDuration)
            {
                _endTime = _startTime + minDuration;
            }

            if (_action != null)
            {
                _action.BlockDuration = Duration;
            }
        }

        /// <summary>
        /// Creates an independent clone of this action block data.
        /// </summary>
        /// <returns>Deep-copied action block data instance</returns>
        public MontageActionBlockData Clone()
        {
            var cloned = new MontageActionBlockData
            {
                _startTime = _startTime,
                _endTime = _endTime,
                _startFrame = _startFrame,
                _endFrame = _endFrame,
                _action = _action != null ? _action.Clone() : null
            };
            cloned.SourceData = this;
            if (cloned._action != null)
            {
                cloned._action.BlockDuration = cloned.Duration;
            }
            return cloned;
        }

        #endregion
    }
}
