using System;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 单个动画片段数据模型。
    /// 承载在单条核心动画轨道上的动画资源引用、时间轴放置起点、独立播放倍率（调速）、裁剪入点及交叉过渡曲线。
    /// </summary>
    [Serializable]
    public class MontageAnimationSegment
    {
        #region Inspector 字段

        [Tooltip("目标动画片段资源。")]
        [SerializeField] private AnimationClip _clip;

        [Tooltip("该片段在蒙太奇总时间轴上的起始放置时间（秒）。")]
        [Min(0.0f)]
        [SerializeField] private float _startTime;

        [Tooltip("内部动画裁剪入点时间（Trim In，秒，从动画本身的第几秒开始播放）。")]
        [Min(0.0f)]
        [SerializeField] private float _startOffset;

        [Tooltip("内部动画裁剪出点时间（Trim Out，秒，播放到动画本身的第几秒结束。若为 0 则代表播放至 Clip 结尾）。")]
        [Min(0.0f)]
        [SerializeField] private float _endOffset;

        [Tooltip("在时间轴上的持续时间（秒）。由 EffectiveClipLength / PlayRate 决定。")]
        [Min(0.001f)]
        [SerializeField] private float _duration;

        [Tooltip("交叉混合过渡插值曲线。")]
        [SerializeField] private AnimationCurve _blendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        #endregion

        #region 公共属性

        /// <summary>
        /// 目标动画片段资源。
        /// </summary>
        public AnimationClip Clip
        {
            get => _clip;
            set => _clip = value;
        }

        /// <summary>
        /// 片段在蒙太奇时间轴上的起始时间（秒）。
        /// </summary>
        public float StartTime
        {
            get => _startTime;
            set => _startTime = Mathf.Max(0.0f, value);
        }

        /// <summary>
        /// 播放速率倍率（严格只读派生计算值：EffectiveClipLength / Duration）。
        /// 权威真理源唯一归属于 Duration。
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
        /// 依据期望播放倍率换算并设置权威持续时长。
        /// </summary>
        /// <param name="playRate">目标播放倍率</param>
        public void SetDurationByPlayRate(float playRate)
        {
            float rate = Mathf.Max(0.01f, playRate);
            _duration = Mathf.Max(0.001f, EffectiveClipLength / rate);
        }

        /// <summary>
        /// 动画裁剪入点偏移（秒）。
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
        /// 动画裁剪出点偏移（秒，若为 0 则代表播放至 Clip 结尾）。
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
        /// 序列化存储的原始 EndOffset（若为 0 表示未截断出点）。
        /// </summary>
        public float RawEndOffset
        {
            get => _endOffset;
            set => _endOffset = Mathf.Max(0.0f, value);
        }

        /// <summary>
        /// Clip 本地有效截取播放长度（秒，即 EndOffset - StartOffset）。
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
        /// 在时间轴上的持续时长（秒，权威持久化数值）。
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
        /// 片段在时间轴上的结束时间（秒）。
        /// </summary>
        public float EndTime => StartTime + Duration;

        /// <summary>
        /// 交叉过渡曲线。
        /// </summary>
        public AnimationCurve BlendCurve
        {
            get => _blendCurve ??= AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            set => _blendCurve = value;
        }

        /// <summary>
        /// 片段显示名称。
        /// </summary>
        public string SegmentName => _clip != null ? _clip.name : "None (Missing Clip)";

        #endregion

        #region 构造方法

        public MontageAnimationSegment()
        {
        }

        public MontageAnimationSegment(AnimationClip clip, float startTime = 0.0f, float playRate = 1.0f)
        {
            _clip = clip;
            _startTime = Mathf.Max(0.0f, startTime);
            _startOffset = 0.0f;
            _blendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

            float rate = Mathf.Max(0.01f, playRate);
            _duration = Mathf.Max(0.001f, EffectiveClipLength / rate);
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 根据源 Clip 有效截取区间时长计算 1.0x 标准播放速率下的自然持续时长。
        /// </summary>
        /// <returns>计算出的自然持续时间（秒）</returns>
        public float CalculateNaturalDuration()
        {
            if (_clip == null) return 0.05f;
            return EffectiveClipLength;
        }

        /// <summary>
        /// 重置持续时长为该片段的自然时长（即恢复 1.0x 速率）。
        /// </summary>
        public void ResetToNaturalDuration()
        {
            _duration = CalculateNaturalDuration();
        }

        /// <summary>
        /// 重置裁剪区间为原动画全长（0 ~ Clip.length），并保持当前播放速率。
        /// </summary>
        public void ResetTrim()
        {
            float curRate = PlayRate;
            _startOffset = 0.0f;
            _endOffset = 0.0f;
            _duration = Mathf.Max(0.001f, EffectiveClipLength / curRate);
        }

        /// <summary>
        /// 重置播放倍率为 1.0x 标准速率。
        /// </summary>
        public void ResetPlayRate()
        {
            _duration = CalculateNaturalDuration();
        }

        /// <summary>
        /// 校验并规范化数据，防止非法负值与零长度。
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

            if (_blendCurve == null || _blendCurve.length < 2)
            {
                _blendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            }
        }

        /// <summary>
        /// 计算给定蒙太奇时间轴时间戳在当前片段内部的本地采样时间戳（秒）。
        /// </summary>
        /// <param name="timelineTime">蒙太奇绝对时间戳（秒）</param>
        /// <returns>内部动画采样时间戳（秒）</returns>
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
        /// 深度克隆副本。
        /// </summary>
        /// <returns>克隆的片段实例</returns>
        public MontageAnimationSegment Clone()
        {
            var cloned = new MontageAnimationSegment
            {
                _clip = _clip,
                _startTime = _startTime,
                _startOffset = _startOffset,
                _endOffset = _endOffset,
                _duration = _duration,
                _blendCurve = _blendCurve != null && _blendCurve.keys != null
                    ? new AnimationCurve(_blendCurve.keys)
                    : AnimationCurve.EaseInOut(0f, 0f, 1f, 1f)
            };
            return cloned;
        }

        #endregion
    }
}
