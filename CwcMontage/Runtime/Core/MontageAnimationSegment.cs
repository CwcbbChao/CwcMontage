using System;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// Animation clip segment model on a montage animation track.
    /// Encapsulates clip asset references, timeline start time, play rate, trimming, and blend curves.
    /// </summary>
    [Serializable]
    public class MontageAnimationSegment
    {
        #region Serialized Fields

        [Tooltip("Source animation clip asset.")]
        [SerializeField] private AnimationClip _clip;

        [Tooltip("Start time on the montage timeline in seconds.")]
        [Min(0.0f)]
        [SerializeField] private float _startTime;

        [Tooltip("Trim in offset in seconds (where playback begins inside the clip).")]
        [Min(0.0f)]
        [SerializeField] private float _startOffset;

        [Tooltip("Trim out offset in seconds (where playback ends. 0 plays until the end of the clip).")]
        [Min(0.0f)]
        [SerializeField] private float _endOffset;

        [Tooltip("Duration on the timeline in seconds (EffectiveClipLength / PlayRate).")]
        [Min(0.001f)]
        [SerializeField] private float _duration;

        [Tooltip("Blend-in fade duration in seconds to prevent sudden pose pops.")]
        [Min(0.0f)]
        [SerializeField] private float _blendInTime = 0.1f;

        [Tooltip("Post blend-out fade duration in seconds after physical playback ends (Pose Hold + linear decay).")]
        [Min(0.0f)]
        [SerializeField] private float _blendOutTime = 0.1f;

        #endregion

        #region Properties

        /// <summary>
        /// Target animation clip asset.
        /// </summary>
        public AnimationClip Clip
        {
            get => _clip;
            set => _clip = value;
        }

        /// <summary>
        /// Start time of the segment on the montage timeline in seconds.
        /// </summary>
        public float StartTime
        {
            get => _startTime;
            set => _startTime = Mathf.Max(0.0f, value);
        }

        /// <summary>
        /// Playback rate multiplier (strictly read-only derived value: EffectiveClipLength / Duration).
        /// Duration is the single authoritative source of truth.
        /// </summary>
        public float PlayRate
        {
            get
            {
                float dur = Duration;
                return dur > 0.0001f ? Mathf.Max(0.01f, EffectiveClipLength / dur) : 1.0f;
            }
        }

        /// <summary>
        /// Converts the desired playback rate and sets the authoritative duration.
        /// </summary>
        /// <param name="playRate">Target playback rate.</param>
        public void SetDurationByPlayRate(float playRate)
        {
            float rate = Mathf.Max(0.01f, playRate);
            _duration = Mathf.Max(0.001f, EffectiveClipLength / rate);
        }

        /// <summary>
        /// Animation trim-in offset in seconds.
        /// </summary>
        public float StartOffset
        {
            get => _startOffset;
            set
            {
                _startOffset = Mathf.Max(0.0f, value);
                if (_clip != null && _startOffset >= _clip.length)
                {
                    _startOffset = Mathf.Max(0.0f, _clip.length - 0.01f);
                }
                if (_endOffset > 0.0001f && _startOffset >= _endOffset)
                {
                    _startOffset = Mathf.Max(0.0f, _endOffset - 0.01f);
                }
            }
        }

        /// <summary>
        /// Animation trim-out offset in seconds (0 means playback runs to the end of the clip).
        /// </summary>
        public float EndOffset
        {
            get
            {
                if (_clip == null) return 0.0f;
                if (_endOffset > 0.0001f && _endOffset < _clip.length)
                {
                    return _endOffset;
                }
                return _clip.length;
            }
            set
            {
                if (_clip != null && value >= _clip.length)
                {
                    _endOffset = 0.0f;
                }
                else
                {
                    _endOffset = Mathf.Max(0.0f, value);
                }
            }
        }

        /// <summary>
        /// Raw serialized EndOffset (0 means no trim-out).
        /// </summary>
        public float RawEndOffset
        {
            get => _endOffset;
            set => _endOffset = Mathf.Max(0.0f, value);
        }

        /// <summary>
        /// Effective trimmed playback length within the clip in seconds (EndOffset - StartOffset).
        /// </summary>
        public float EffectiveClipLength
        {
            get
            {
                if (_clip == null) return 0.05f;
                float end = EndOffset;
                return Mathf.Max(0.001f, end - _startOffset);
            }
        }

        /// <summary>
        /// Duration on the timeline in seconds (authoritative persisted value).
        /// </summary>
        public float Duration
        {
            get
            {
                if (_duration > 0.0001f)
                {
                    return _duration;
                }
                return CalculateNaturalDuration();
            }
            set => _duration = Mathf.Max(0.001f, value);
        }

        /// <summary>
        /// End time of the physical playback on the timeline in seconds.
        /// </summary>
        public float EndTime => StartTime + Duration;

        /// <summary>
        /// Final authoritative end time including post blend-out decay (seconds).
        /// </summary>
        public float PostEndTime => EndTime + Mathf.Max(0.0f, _blendOutTime);

        /// <summary>
        /// Smooth fade-in duration at the segment's in point in seconds.
        /// </summary>
        public float BlendInTime
        {
            get => _blendInTime;
            set => _blendInTime = Mathf.Max(0.0f, value);
        }

        /// <summary>
        /// Smooth post blend-out duration in seconds (after physical playback ends).
        /// </summary>
        public float BlendOutTime
        {
            get => _blendOutTime;
            set => _blendOutTime = Mathf.Max(0.0f, value);
        }

        /// <summary>
        /// Display name of the segment.
        /// </summary>
        public string SegmentName => _clip != null ? _clip.name : "None (Missing Clip)";

        #endregion

        #region Constructors

        public MontageAnimationSegment()
        {
        }

        public MontageAnimationSegment(AnimationClip clip, float startTime = 0.0f, float playRate = 1.0f)
        {
            _clip = clip;
            _startTime = Mathf.Max(0.0f, startTime);
            _startOffset = 0.0f;
            _blendInTime = 0.1f;
            _blendOutTime = 0.1f;

            float rate = Mathf.Max(0.01f, playRate);
            _duration = Mathf.Max(0.001f, EffectiveClipLength / rate);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Calculates the natural duration at the standard 1.0x playback rate from the clip's effective trimmed length.
        /// </summary>
        /// <returns>The calculated natural duration in seconds.</returns>
        public float CalculateNaturalDuration()
        {
            if (_clip == null) return 0.05f;
            return EffectiveClipLength;
        }

        /// <summary>
        /// Resets the duration to the segment's natural duration (restoring the 1.0x rate).
        /// </summary>
        public void ResetToNaturalDuration()
        {
            _duration = CalculateNaturalDuration();
        }

        /// <summary>
        /// Resets the trim range to the full clip length (0 to Clip.length) while preserving the current playback rate.
        /// </summary>
        public void ResetTrim()
        {
            float curRate = PlayRate;
            _startOffset = 0.0f;
            _endOffset = 0.0f;
            _duration = Mathf.Max(0.001f, EffectiveClipLength / curRate);
        }

        /// <summary>
        /// Resets the playback rate to the standard 1.0x rate.
        /// </summary>
        public void ResetPlayRate()
        {
            _duration = CalculateNaturalDuration();
        }

        /// <summary>
        /// Validates and normalizes the data to prevent invalid negative values and zero lengths.
        /// </summary>
        public void EnsureValid()
        {
            if (_startTime < 0.0f) _startTime = 0.0f;
            if (_startOffset < 0.0f) _startOffset = 0.0f;
            if (_endOffset < 0.0f) _endOffset = 0.0f;

            if (_clip != null)
            {
                if (_startOffset >= _clip.length)
                {
                    _startOffset = Mathf.Max(0.0f, _clip.length - 0.01f);
                }
                if (_endOffset > 0.0001f && _endOffset <= _startOffset)
                {
                    _endOffset = Mathf.Min(_clip.length, _startOffset + 0.01f);
                }
            }

            if (_duration <= 0.0001f)
            {
                _duration = CalculateNaturalDuration();
            }

            if (_blendInTime < 0.0f) _blendInTime = 0.0f;
            if (_blendOutTime < 0.0f) _blendOutTime = 0.0f;
        }

        /// <summary>
        /// Calculates the linear weight factor [0.0, 1.0] for the given timeline timestamp during this segment's blend-in phase.
        /// </summary>
        public float CalculateFadeInFactor(float timelineTime)
        {
            if (_blendInTime <= 0.0001f) return 1.0f;
            float elapsed = timelineTime - _startTime;
            if (elapsed <= 0f) return 0.0f;
            return Mathf.Clamp01(elapsed / _blendInTime);
        }

        /// <summary>
        /// Calculates the linear post blend-out decay factor [1.0, 0.0] during the segment's post-playback phase [EndTime, PostEndTime].
        /// </summary>
        public float CalculatePostFadeOutFactor(float timelineTime)
        {
            if (_blendOutTime <= 0.0001f) return 0.0f;
            float elapsedSinceEnd = timelineTime - EndTime;
            if (elapsedSinceEnd <= 0f) return 1.0f;
            return Mathf.Clamp01(1.0f - (elapsedSinceEnd / _blendOutTime));
        }

        /// <summary>
        /// Calculates the local sample timestamp in seconds inside this segment for the given montage timeline timestamp.
        /// Automatically clamps to the last trimmed frame during post blend-out to maintain pose hold.
        /// </summary>
        /// <param name="timelineTime">Absolute montage timestamp in seconds.</param>
        /// <returns>Internal animation sample timestamp in seconds.</returns>
        public float EvaluateLocalSampleTime(float timelineTime)
        {
            if (_clip == null) return 0.0f;
            float elapsedInSegment = Mathf.Max(0.0f, timelineTime - _startTime);
            float sampleTime = _startOffset + (elapsedInSegment * PlayRate);

            float start = _startOffset;
            float end = EndOffset;
            float span = Mathf.Max(0.001f, end - start);

            if (_clip.isLooping)
            {
                float offsetInSpan = (sampleTime - start) % span;
                if (offsetInSpan < 0f) offsetInSpan += span;
                return start + offsetInSpan;
            }

            return Mathf.Clamp(sampleTime, start, end);
        }

        /// <summary>
        /// Deep clone copy.
        /// </summary>
        /// <returns>The cloned segment instance.</returns>
        public MontageAnimationSegment Clone()
        {
            var cloned = new MontageAnimationSegment
            {
                _clip = _clip,
                _startTime = _startTime,
                _startOffset = _startOffset,
                _endOffset = _endOffset,
                _duration = _duration,
                _blendInTime = _blendInTime,
                _blendOutTime = _blendOutTime
            };
            return cloned;
        }

        #endregion
    }
}
