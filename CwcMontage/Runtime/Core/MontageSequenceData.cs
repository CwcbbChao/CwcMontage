using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 纯 C# 蒙太奇动画序列核心数据模型（POCO）。
    /// 彻底脱离 UnityEngine.Object / ScriptableObject 依赖，实现纯托管内存流转、0 GC 动态拼装与跨平台数据通用化。
    /// 完整实现 IMontageSequence 契约。
    /// </summary>
    [Serializable]
    public class MontageSequenceData : IMontageSequence
    {
        #region 1. 常量与静态成员

        public const float DEFAULT_BLEND_TIME = 0.1f;

        /// <summary>
        /// 从单个零散动画切片极速构造纯 C# 蒙太奇序列数据（运行期 0 Native Object 创建）。
        /// 严格遵循纯线性切片物理模型：切片在物理时长内 100% 满额播放，并在其后置外置区间平滑淡出退回底层。
        /// </summary>
        /// <param name="clip">动画切片资产</param>
        /// <param name="blendInTime">平滑淡入时间（秒）</param>
        /// <param name="blendOutTime">平滑淡出时间（秒）</param>
        /// <param name="isFullBody">是否挂载在全身主轨道（若为 false 则挂载在上半身混合轨道）</param>
        /// <param name="playRate">播放速率倍率</param>
        /// <returns>构造好的纯 C# 蒙太奇数据实例</returns>
        public static MontageSequenceData CreateFromClip(
            AnimationClip clip,
            float blendInTime = DEFAULT_BLEND_TIME,
            float blendOutTime = DEFAULT_BLEND_TIME,
            bool isFullBody = true,
            float playRate = 1.0f)
        {
            float safeRate = playRate > 0.001f ? playRate : 1.0f;
            float clipLen = clip != null && clip.length > 0.001f ? clip.length : 0.05f;
            float effectiveDuration = Mathf.Max(0.001f, clipLen / safeRate);
            float maxSafeBlend = Mathf.Min(DEFAULT_BLEND_TIME, effectiveDuration * 0.25f);
            float safeBlendIn = Mathf.Min(blendInTime, maxSafeBlend);
            float safeBlendOut = Mathf.Min(blendOutTime, maxSafeBlend);

            var data = new MontageSequenceData
            {
                EnableUpperBodyTrack = !isFullBody
            };

            if (clip != null)
            {
                var segment = new MontageAnimationSegment
                {
                    Clip = clip,
                    StartTime = 0.0f,
                    Duration = effectiveDuration,
                    BlendInTime = safeBlendIn,
                    BlendOutTime = safeBlendOut
                };

                if (isFullBody)
                {
                    data.FullBodySegments.Add(segment);
                }
                else
                {
                    data.UpperBodySegments.Add(segment);
                }
            }

            return data;
        }

        #endregion

        #region 2. Inspector 序列化字段

        [Header("Playback Base")]
        [Tooltip("播放过程中是否启用足底 IK（Foot IK）。")]
        [SerializeField] private bool _isFootIK;

        [Header("Layer: Upper Body")]
        [Tooltip("是否启用上半身混合分层轨道。")]
        [SerializeField] private bool _enableUpperBodyTrack;

        [Tooltip("上半身轨道的基础混合权重 [0.0, 1.0]。")]
        [Range(0f, 1f)]
        [SerializeField] private float _upperBodyWeight = 1.0f;

        [Tooltip("上半身轨道的动画切片列表。")]
        [SerializeField] private List<MontageAnimationSegment> _upperBodySegments = new();

        [Tooltip("是否开启上半身脊柱解耦朝向补偿。")]
        [SerializeField] private bool _decoupleUpperBodyOrientation = true;

        [Tooltip("该蒙太奇专用的自定义上半身遮罩。")]
        [SerializeField] private AvatarMask _customUpperBodyMask;

        [Header("Layer: Full Body")]
        [Tooltip("全身核心主轨道的混合权重 [0.0, 1.0]。")]
        [Range(0f, 1f)]
        [SerializeField] private float _fullBodyWeight = 1.0f;

        [Tooltip("全身核心主轨道的动画切片列表。")]
        [SerializeField] private List<MontageAnimationSegment> _animationSegments = new();

        [Header("Layer: Additive")]
        [Tooltip("是否启用叠加（Additive）轨道。")]
        [SerializeField] private bool _enableAdditiveTrack;

        [Tooltip("叠加轨道的基础混合权重 [0.0, 1.0]。")]
        [Range(0f, 1f)]
        [SerializeField] private float _additiveWeight = 1.0f;

        [Tooltip("叠加轨道的动画切片列表。")]
        [SerializeField] private List<MontageAnimationSegment> _additiveSegments = new();

        [Header("Root Motion")]
        [Tooltip("是否应用水平平面（X/Z）根运动位移。")]
        [SerializeField] private bool _applyHorizontalRootMotion;

        [Tooltip("是否应用垂直（Y）根运动位移。")]
        [SerializeField] private bool _applyVerticalRootMotion;

        [Tooltip("是否应用根运动旋转朝向。")]
        [SerializeField] private bool _applyRotationRootMotion;

        [Header("Sections & Tracks")]
        [Tooltip("物理分段切分时间戳列表（秒）。")]
        [SerializeField] private List<float> _splitTimestamps = new();

        [Tooltip("视听与事件轨道列表。")]
        [SerializeField] private List<MontageTrackData> _tracks = new();

        [HideInInspector]
        [SerializeField] private List<string> _genericSampleBonePaths = new();

#if UNITY_EDITOR
        [SerializeField] private bool _overridePreviewModel;
        [SerializeField] private GameObject _previewModel;
#endif

        #endregion

        #region 3. 构造函数

        public MontageSequenceData()
        {
        }

        /// <summary>
        /// 基于现有的 IMontageSequence 契约实例进行深拷贝构建。
        /// </summary>
        public MontageSequenceData(IMontageSequence source)
        {
            if (source == null) return;

            _isFootIK = source.IsFootIK;

            _enableUpperBodyTrack = source.EnableUpperBodyTrack;
            _upperBodyWeight = source.UpperBodyWeight;
            _decoupleUpperBodyOrientation = source.DecoupleUpperBodyOrientation;
            _customUpperBodyMask = source.CustomUpperBodyMask;

            _fullBodyWeight = source.FullBodyWeight;
            _enableAdditiveTrack = source.EnableAdditiveTrack;
            _additiveWeight = source.AdditiveWeight;

            _applyHorizontalRootMotion = source.ApplyHorizontalRootMotion;
            _applyVerticalRootMotion = source.ApplyVerticalRootMotion;
            _applyRotationRootMotion = source.ApplyRotationRootMotion;

            if (source.UpperBodySegments != null)
            {
                for (int i = 0; i < source.UpperBodySegments.Count; i++)
                {
                    _upperBodySegments.Add(source.UpperBodySegments[i]);
                }
            }

            if (source.FullBodySegments != null)
            {
                for (int i = 0; i < source.FullBodySegments.Count; i++)
                {
                    _animationSegments.Add(source.FullBodySegments[i]);
                }
            }

            if (source.AdditiveSegments != null)
            {
                for (int i = 0; i < source.AdditiveSegments.Count; i++)
                {
                    _additiveSegments.Add(source.AdditiveSegments[i]);
                }
            }

            if (source.SplitTimestamps != null)
            {
                _splitTimestamps.AddRange(source.SplitTimestamps);
            }

            if (source.Tracks != null)
            {
                _tracks.AddRange(source.Tracks);
            }

            if (source.GenericSampleBonePaths != null)
            {
                _genericSampleBonePaths.AddRange(source.GenericSampleBonePaths);
            }
        }

        #endregion

        #region 4. 公共属性与契约实现

        public virtual bool IsFootIK { get => _isFootIK; set => _isFootIK = value; }

        public virtual bool EnableUpperBodyTrack { get => _enableUpperBodyTrack; set => _enableUpperBodyTrack = value; }
        public virtual bool EnableUpperBody { get => _enableUpperBodyTrack; set => _enableUpperBodyTrack = value; }
        public virtual float UpperBodyWeight { get => _upperBodyWeight; set => _upperBodyWeight = Mathf.Clamp01(value); }
        public virtual List<MontageAnimationSegment> UpperBodySegments => _upperBodySegments;
        IReadOnlyList<MontageAnimationSegment> IMontageSequence.UpperBodySegments => _upperBodySegments;
        public virtual bool DecoupleUpperBodyOrientation { get => _decoupleUpperBodyOrientation; set => _decoupleUpperBodyOrientation = value; }
        public virtual AvatarMask CustomUpperBodyMask { get => _customUpperBodyMask; set => _customUpperBodyMask = value; }

        public virtual float FullBodyWeight { get => _fullBodyWeight; set => _fullBodyWeight = Mathf.Clamp01(value); }
        public virtual List<MontageAnimationSegment> FullBodySegments => _animationSegments;
        IReadOnlyList<MontageAnimationSegment> IMontageSequence.FullBodySegments => _animationSegments;

        public virtual bool EnableAdditiveTrack { get => _enableAdditiveTrack; set => _enableAdditiveTrack = value; }
        public virtual bool EnableAdditive { get => _enableAdditiveTrack; set => _enableAdditiveTrack = value; }
        public virtual float AdditiveWeight { get => _additiveWeight; set => _additiveWeight = Mathf.Clamp01(value); }
        public virtual List<MontageAnimationSegment> AdditiveSegments => _additiveSegments;
        IReadOnlyList<MontageAnimationSegment> IMontageSequence.AdditiveSegments => _additiveSegments;

#if UNITY_EDITOR
        public virtual bool OverridePreviewModel { get => _overridePreviewModel; set => _overridePreviewModel = value; }
        public virtual GameObject PreviewModel { get => _previewModel; set => _previewModel = value; }
#endif

        public virtual bool ApplyHorizontalRootMotion { get => _applyHorizontalRootMotion; set => _applyHorizontalRootMotion = value; }
        public virtual bool ApplyVerticalRootMotion { get => _applyVerticalRootMotion; set => _applyVerticalRootMotion = value; }
        public virtual bool ApplyRotationRootMotion { get => _applyRotationRootMotion; set => _applyRotationRootMotion = value; }

        public virtual List<float> SplitTimestamps => _splitTimestamps;
        IReadOnlyList<float> IMontageSequence.SplitTimestamps => _splitTimestamps;

        public virtual List<MontageTrackData> Tracks => _tracks;
        IReadOnlyList<MontageTrackData> IMontageSequence.Tracks => _tracks;

        public virtual List<string> GenericSampleBonePaths => _genericSampleBonePaths;
        IReadOnlyList<string> IMontageSequence.GenericSampleBonePaths => _genericSampleBonePaths;

        /// <summary>
        /// 首个有效动画切片（只读便捷属性）。
        /// </summary>
        public virtual AnimationClip AnimationClip => this.GetPrimaryClip();

        /// <summary>
        /// 蒙太奇在时间轴上的完整物理时长（秒，扫描所有动画轨道、动作块与物理分段）。
        /// </summary>
        public virtual float TotalDuration
        {
            get
            {
                float maxEndTime = 0.0f;

                if (_animationSegments != null)
                {
                    for (int i = 0; i < _animationSegments.Count; i++)
                    {
                        var seg = _animationSegments[i];
                        if (seg != null && seg.EndTime > maxEndTime) maxEndTime = seg.EndTime;
                    }
                }

                if (_enableUpperBodyTrack && _upperBodySegments != null)
                {
                    for (int i = 0; i < _upperBodySegments.Count; i++)
                    {
                        var seg = _upperBodySegments[i];
                        if (seg != null && seg.EndTime > maxEndTime) maxEndTime = seg.EndTime;
                    }
                }

                if (_enableAdditiveTrack && _additiveSegments != null)
                {
                    for (int i = 0; i < _additiveSegments.Count; i++)
                    {
                        var seg = _additiveSegments[i];
                        if (seg != null && seg.EndTime > maxEndTime) maxEndTime = seg.EndTime;
                    }
                }

                if (_tracks != null)
                {
                    for (int i = 0; i < _tracks.Count; i++)
                    {
                        var track = _tracks[i];
                        if (track?.ActionBlocks == null) continue;
                        for (int j = 0; j < track.ActionBlocks.Count; j++)
                        {
                            var block = track.ActionBlocks[j];
                            if (block != null && block.EndTime > maxEndTime) maxEndTime = block.EndTime;
                        }
                    }
                }

                if (_splitTimestamps != null)
                {
                    for (int i = 0; i < _splitTimestamps.Count; i++)
                    {
                        if (_splitTimestamps[i] > maxEndTime) maxEndTime = _splitTimestamps[i];
                    }
                }

                return maxEndTime;
            }
        }

        /// <summary>
        /// 蒙太奇单次播放自然结束时间点（秒，涵盖所有动画切片后置淡出与动作块的最大结束时间）。
        /// 严格由切片实际外置淡出结束时刻（PostEndTime）与动作块决定，逻辑精确自洽。
        /// </summary>
        public virtual float NaturalEndTime
        {
            get
            {
                float maxEndTime = 0.0f;

                if (_animationSegments != null)
                {
                    for (int i = 0; i < _animationSegments.Count; i++)
                    {
                        var seg = _animationSegments[i];
                        if (seg != null && seg.PostEndTime > maxEndTime) maxEndTime = seg.PostEndTime;
                    }
                }

                if (_enableUpperBodyTrack && _upperBodySegments != null)
                {
                    for (int i = 0; i < _upperBodySegments.Count; i++)
                    {
                        var seg = _upperBodySegments[i];
                        if (seg != null && seg.PostEndTime > maxEndTime) maxEndTime = seg.PostEndTime;
                    }
                }

                if (_enableAdditiveTrack && _additiveSegments != null)
                {
                    for (int i = 0; i < _additiveSegments.Count; i++)
                    {
                        var seg = _additiveSegments[i];
                        if (seg != null && seg.PostEndTime > maxEndTime) maxEndTime = seg.PostEndTime;
                    }
                }

                if (_tracks != null)
                {
                    for (int i = 0; i < _tracks.Count; i++)
                    {
                        var track = _tracks[i];
                        if (track?.ActionBlocks == null) continue;
                        for (int j = 0; j < track.ActionBlocks.Count; j++)
                        {
                            var block = track.ActionBlocks[j];
                            if (block != null && block.EndTime > maxEndTime) maxEndTime = block.EndTime;
                        }
                    }
                }

                if (_splitTimestamps != null)
                {
                    for (int i = 0; i < _splitTimestamps.Count; i++)
                    {
                        if (_splitTimestamps[i] > maxEndTime) maxEndTime = _splitTimestamps[i];
                    }
                }

                return Mathf.Max(maxEndTime, TotalDuration);
            }
        }

        /// <summary>
        /// 基础动画帧率（优先首个有效切片的采样帧率）。
        /// </summary>
        public virtual float FrameRate
        {
            get
            {
                if (_animationSegments != null)
                {
                    for (int i = 0; i < _animationSegments.Count; i++)
                    {
                        var clip = _animationSegments[i]?.Clip;
                        if (clip != null && clip.frameRate > 0.01f)
                        {
                            return clip.frameRate;
                        }
                    }
                }
                return 30.0f;
            }
        }

        /// <summary>
        /// 总动画帧数。
        /// </summary>
        public virtual int TotalFrames => Mathf.RoundToInt(TotalDuration * FrameRate);

        #endregion

        #region 5. 运行时评估与动作块烘焙

        [NonSerialized] private List<MontageActionBlockData> _bakedRuntimeActionBlocks;

        /// <summary>
        /// 烘焙好的运行时不可变动作块列表（0 GC 读取）。
        /// </summary>
        public virtual IReadOnlyList<MontageActionBlockData> BakedRuntimeActionBlocks
        {
            get
            {
                if (_bakedRuntimeActionBlocks == null)
                {
                    BakeRuntimeActionBlocks();
                }
                return _bakedRuntimeActionBlocks;
            }
        }

        public virtual void InvalidateBakedBlocks()
        {
            _bakedRuntimeActionBlocks = null;
        }

        public virtual void BakeRuntimeActionBlocks()
        {
            _bakedRuntimeActionBlocks = new List<MontageActionBlockData>();
            if (_tracks == null) return;

            for (int i = 0; i < _tracks.Count; i++)
            {
                var track = _tracks[i];
                if (track == null || track.IsMuted || track.ActionBlocks == null) continue;

                for (int j = 0; j < track.ActionBlocks.Count; j++)
                {
                    var block = track.ActionBlocks[j];
                    if (block?.Action == null || !block.Action.IsEnabled) continue;
                    _bakedRuntimeActionBlocks.Add(block);
                }
            }

            _bakedRuntimeActionBlocks.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
        }

        public virtual int SectionCount => _splitTimestamps != null ? _splitTimestamps.Count + 1 : 1;

        public virtual float GetSectionDuration(int sectionIndex)
        {
            var range = GetSectionRange(sectionIndex);
            return Mathf.Max(0f, range.end - range.start);
        }

        public virtual (float start, float end) GetSectionRange(int sectionIndex)
        {
            float total = TotalDuration;
            if (_splitTimestamps == null || _splitTimestamps.Count == 0)
            {
                return (0f, total);
            }

            int count = _splitTimestamps.Count;
            if (sectionIndex <= 0)
            {
                float firstSplit = Mathf.Clamp(_splitTimestamps[0], 0f, total);
                return (0f, firstSplit);
            }

            if (sectionIndex >= count)
            {
                float lastSplit = Mathf.Clamp(_splitTimestamps[count - 1], 0f, total);
                return (lastSplit, total);
            }

            float prevSplit = Mathf.Clamp(_splitTimestamps[sectionIndex - 1], 0f, total);
            float currSplit = Mathf.Clamp(_splitTimestamps[sectionIndex], 0f, total);
            return (prevSplit, currSplit);
        }

        public virtual int GetSectionIndexAtTime(float time)
        {
            if (_splitTimestamps == null || _splitTimestamps.Count == 0)
            {
                return 0;
            }

            for (int i = 0; i < _splitTimestamps.Count; i++)
            {
                if (time < _splitTimestamps[i])
                {
                    return i;
                }
            }

            return _splitTimestamps.Count;
        }

        public virtual void EvaluateAnimationSegments(
            float timelineTime,
            List<int> outIndices,
            List<float> outSampleTimes,
            List<float> outWeights)
        {
            EvaluateSegments(_animationSegments, timelineTime, outIndices, outSampleTimes, outWeights);
        }

        public virtual void EvaluateChannelSegments(
            MontageLayerChannel channel,
            float timelineTime,
            List<int> outIndices,
            List<float> outSampleTimes,
            List<float> outWeights)
        {
            switch (channel)
            {
                case MontageLayerChannel.UpperBody:
                    if (!_enableUpperBodyTrack)
                    {
                        outIndices?.Clear();
                        outSampleTimes?.Clear();
                        outWeights?.Clear();
                        return;
                    }
                    EvaluateSegments(_upperBodySegments, timelineTime, outIndices, outSampleTimes, outWeights);
                    break;
                case MontageLayerChannel.FullBody:
                    EvaluateSegments(_animationSegments, timelineTime, outIndices, outSampleTimes, outWeights);
                    break;
                case MontageLayerChannel.Additive:
                    if (!_enableAdditiveTrack)
                    {
                        outIndices?.Clear();
                        outSampleTimes?.Clear();
                        outWeights?.Clear();
                        return;
                    }
                    EvaluateSegments(_additiveSegments, timelineTime, outIndices, outSampleTimes, outWeights);
                    break;
            }
        }

        /// <summary>
        /// 核心多切片时序混合求值算法。
        /// 遵循状态瞬时继承（Latching & Handoff）物理连续性模型：
        /// 1. 当前切片启动时，以时间轴前置切片（若存在且尚未完全淡出到 0）的瞬时混合状态作为基底进行过渡；
        /// 2. 前置切片自身的独立淡出时钟中止，并保持在当前帧（若已过物理时长则锁定末尾定格帧 Pose Hold）；
        /// 3. 若前置切片与当前切片相邻（或重叠），两者权重和在过渡期内恒等于 1.0f，严格 0% 透底；
        /// 4. 仅在轨道末尾或存在真实空隙（Gap）时，才触发切片自身的后置末帧淡出退回底层。
        /// </summary>
        public static void EvaluateSegments(
            List<MontageAnimationSegment> segments,
            float timelineTime,
            List<int> outIndices,
            List<float> outSampleTimes,
            List<float> outWeights)
        {
            outIndices?.Clear();
            outSampleTimes?.Clear();
            outWeights?.Clear();

            if (segments == null || segments.Count == 0 || outIndices == null || outSampleTimes == null || outWeights == null)
            {
                return;
            }

            float t = timelineTime;
            int count = segments.Count;

            // 1. 定位时间轴当前时间 t 之前或刚到达的最后一个有效切片（currentIdx）
            int currentIdx = -1;
            float maxStart = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                var seg = segments[i];
                if (seg?.Clip == null) continue;
                if (t >= seg.StartTime && seg.StartTime >= maxStart)
                {
                    maxStart = seg.StartTime;
                    currentIdx = i;
                }
            }

            // 尚未到达轨道上第一个有效切片的起始点
            if (currentIdx < 0)
            {
                return;
            }

            // 寻找 currentIdx 的前置切片（用于淡入时的姿态交接）
            int prevIdx = -1;
            float prevMaxStart = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                if (i == currentIdx) continue;
                var seg = segments[i];
                if (seg?.Clip == null) continue;
                if (seg.StartTime <= segments[currentIdx].StartTime && seg.StartTime >= prevMaxStart)
                {
                    prevMaxStart = seg.StartTime;
                    prevIdx = i;
                }
            }

            var currentSeg = segments[currentIdx];
            float cStart = currentSeg.StartTime;
            float cEnd = currentSeg.EndTime;
            float cBlendIn = Mathf.Max(0.0f, currentSeg.BlendInTime);

            // 阶段一：当前切片处于淡入阶段 [cStart, cStart + cBlendIn]
            if (t < cStart + cBlendIn && cBlendIn > 0.0001f)
            {
                float alpha = Mathf.Clamp01((t - cStart) / cBlendIn);

                // 计算前置切片在当前切片启动瞬间（t = cStart）的瞬时权重基底
                float wPrevAtStart = 0f;
                MontageAnimationSegment prevSeg = null;

                if (prevIdx >= 0)
                {
                    prevSeg = segments[prevIdx];
                    if (cStart <= prevSeg.EndTime)
                    {
                        // 当前切片在上一片段物理帧内或刚好结束瞬间启动（相邻或重叠）：上一片段尚未与底层混合
                        wPrevAtStart = 1.0f;
                    }
                    else
                    {
                        // 当前切片在上一片段物理帧播完之后启动：上一片段已在走自身的后置淡出
                        float prevBlendOut = Mathf.Max(0.0001f, prevSeg.BlendOutTime);
                        float elapsedFade = cStart - prevSeg.EndTime;
                        wPrevAtStart = Mathf.Clamp01(1.0f - elapsedFade / prevBlendOut);
                    }
                }

                if (wPrevAtStart > 0.0001f && prevSeg != null)
                {
                    // 上一动作的独立淡出中止，锁定在尾帧（若已过 EndTime），平滑出让权重给新切片
                    float prevWeight = wPrevAtStart * (1.0f - alpha);
                    float currentWeight = alpha;

                    if (prevWeight > 0.0001f)
                    {
                        outIndices.Add(prevIdx);
                        outSampleTimes.Add(prevSeg.EvaluateLocalSampleTime(t));
                        outWeights.Add(prevWeight);
                    }

                    if (currentWeight > 0.0001f)
                    {
                        outIndices.Add(currentIdx);
                        outSampleTimes.Add(currentSeg.EvaluateLocalSampleTime(t));
                        outWeights.Add(currentWeight);
                    }
                }
                else
                {
                    // 前面无切片或已彻底淡出完毕（存在明显留白空隙）：从底层机体动作（Locomotion）淡入
                    // 即使在 t == cStart (alpha == 0) 的首帧边界，也必须输出切片索引与采样时间以确保首帧姿态就绪
                    outIndices.Add(currentIdx);
                    outSampleTimes.Add(currentSeg.EvaluateLocalSampleTime(t));
                    outWeights.Add(alpha);
                }
                return;
            }

            // 阶段二：当前切片处于物理有效播放区间 [cStart + cBlendIn, cEnd]
            if (t < cEnd)
            {
                outIndices.Add(currentIdx);
                outSampleTimes.Add(currentSeg.EvaluateLocalSampleTime(t));
                outWeights.Add(1.0f);
                return;
            }

            // 阶段三：当前切片已播满物理动作（t >= cEnd），且后续无接续切片启动（若有紧邻接续切片启动，currentIdx 早已递进）
            float blendOut = Mathf.Max(0.0001f, currentSeg.BlendOutTime);
            float elapsed = t - cEnd;
            float fadeOutWeight = Mathf.Clamp01(1.0f - elapsed / blendOut);

            if (fadeOutWeight > 0.0001f)
            {
                outIndices.Add(currentIdx);
                outSampleTimes.Add(currentSeg.EvaluateLocalSampleTime(t)); // 自动锁定在末尾最后一帧 (Pose Hold)
                outWeights.Add(fadeOutWeight);
            }
        }

        #endregion

        #region 6. 公共操作方法

        public virtual bool? GetAnchorSkeletonIsHuman()
        {
            if (_animationSegments != null)
            {
                for (int i = 0; i < _animationSegments.Count; i++)
                {
                    var clip = _animationSegments[i]?.Clip;
                    if (clip != null) return clip.isHumanMotion;
                }
            }

            if (_upperBodySegments != null)
            {
                for (int i = 0; i < _upperBodySegments.Count; i++)
                {
                    var clip = _upperBodySegments[i]?.Clip;
                    if (clip != null) return clip.isHumanMotion;
                }
            }

            if (_additiveSegments != null)
            {
                for (int i = 0; i < _additiveSegments.Count; i++)
                {
                    var clip = _additiveSegments[i]?.Clip;
                    if (clip != null) return clip.isHumanMotion;
                }
            }

            return null;
        }

        /// <summary>
        /// 检查指定的图层通道上是否包含任何有效切片。
        /// </summary>
        public virtual bool HasAnyAnimationInChannel(MontageLayerChannel channel)
        {
            switch (channel)
            {
                case MontageLayerChannel.UpperBody:
                    return _enableUpperBodyTrack && _upperBodySegments != null && _upperBodySegments.Count > 0;
                case MontageLayerChannel.FullBody:
                    return _animationSegments != null && _animationSegments.Count > 0;
                case MontageLayerChannel.Additive:
                    return _enableAdditiveTrack && _additiveSegments != null && _additiveSegments.Count > 0;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 获取指定图层通道的基础混合权重 [0.0, 1.0]。
        /// </summary>
        public virtual float GetChannelWeight(MontageLayerChannel channel)
        {
            return channel switch
            {
                MontageLayerChannel.UpperBody => _upperBodyWeight,
                MontageLayerChannel.FullBody => _fullBodyWeight,
                MontageLayerChannel.Additive => _additiveWeight,
                _ => 1.0f
            };
        }

        /// <summary>
        /// 设置指定图层通道的基础混合权重 [0.0, 1.0]。
        /// </summary>
        public virtual void SetChannelWeight(MontageLayerChannel channel, float weight)
        {
            float clamped = Mathf.Clamp01(weight);
            switch (channel)
            {
                case MontageLayerChannel.UpperBody:
                    _upperBodyWeight = clamped;
                    break;
                case MontageLayerChannel.FullBody:
                    _fullBodyWeight = clamped;
                    break;
                case MontageLayerChannel.Additive:
                    _additiveWeight = clamped;
                    break;
            }
        }

        /// <summary>
        /// 设置指定图层通道是否启用。
        /// </summary>
        public virtual void SetChannelEnabled(MontageLayerChannel channel, bool enabled)
        {
            switch (channel)
            {
                case MontageLayerChannel.UpperBody:
                    _enableUpperBodyTrack = enabled;
                    break;
                case MontageLayerChannel.FullBody:
                    break;
                case MontageLayerChannel.Additive:
                    _enableAdditiveTrack = enabled;
                    break;
            }
        }

        /// <summary>
        /// 查询指定通道是否已启用。
        /// </summary>
        public virtual bool IsChannelEnabled(MontageLayerChannel channel)
        {
            return channel switch
            {
                MontageLayerChannel.UpperBody => _enableUpperBodyTrack,
                MontageLayerChannel.FullBody => true,
                MontageLayerChannel.Additive => _enableAdditiveTrack,
                _ => true
            };
        }

        /// <summary>
        /// 获取指定通道的动画切片列表。
        /// </summary>
        public virtual List<MontageAnimationSegment> GetSegments(MontageLayerChannel channel)
        {
            return channel switch
            {
                MontageLayerChannel.UpperBody => _upperBodySegments,
                MontageLayerChannel.FullBody => _animationSegments,
                MontageLayerChannel.Additive => _additiveSegments,
                _ => _animationSegments
            };
        }

        /// <summary>
        /// 将动画切片从源通道迁移至目标通道。
        /// </summary>
        public virtual bool MoveSegmentChannel(MontageAnimationSegment segment, MontageLayerChannel fromChannel, MontageLayerChannel toChannel)
        {
            if (segment == null || fromChannel == toChannel) return false;
            var fromList = GetSegments(fromChannel);
            var toList = GetSegments(toChannel);
            if (fromList == null || toList == null || !fromList.Contains(segment)) return false;

            fromList.Remove(segment);
            toList.Add(segment);
            SortChannelSegments(fromChannel);
            SortChannelSegments(toChannel);
            EnsureSegmentsValid();
            return true;
        }

        /// <summary>
        /// 批量更新切分点时间戳。
        /// </summary>
        public virtual void SetSplitTimestamps(IEnumerable<float> timestamps)
        {
            _splitTimestamps.Clear();
            if (timestamps == null) return;

            foreach (var t in timestamps)
            {
                if (t > 0.0001f)
                {
                    _splitTimestamps.Add(t);
                }
            }

            _splitTimestamps.Sort();
        }

        /// <summary>
        /// 排序所有通道上的切片。
        /// </summary>
        public virtual void SortAnimationSegments()
        {
            SortSegmentsList(_animationSegments);
            SortSegmentsList(_upperBodySegments);
            SortSegmentsList(_additiveSegments);
        }

        public virtual void SortAllChannelSegments() => SortAnimationSegments();

        public virtual void SortChannelSegments(MontageLayerChannel channel)
        {
            SortSegmentsList(GetSegments(channel));
        }

        protected static void SortSegmentsList(List<MontageAnimationSegment> list)
        {
            if (list == null || list.Count <= 1) return;
            list.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
        }

        /// <summary>
        /// 校验并规范化所有切片。
        /// </summary>
        public virtual void EnsureSegmentsValid()
        {
            ValidateSegmentsList(_animationSegments);
            ValidateSegmentsList(_upperBodySegments);
            ValidateSegmentsList(_additiveSegments);
        }

        protected static void ValidateSegmentsList(List<MontageAnimationSegment> list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                list[i]?.EnsureValid();
            }
        }

        /// <summary>
        /// 校验并规范化所有轨道上的 ActionBlock。
        /// </summary>
        public virtual void ValidateActionBlocks()
        {
            if (_tracks == null) return;

            float fps = FrameRate;
            for (int i = 0; i < _tracks.Count; i++)
            {
                var track = _tracks[i];
                if (track?.ActionBlocks == null) continue;

                for (int j = 0; j < track.ActionBlocks.Count; j++)
                {
                    track.ActionBlocks[j]?.EnsureValid(fps);
                }
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// 自动提取泛型动作的前 3 根关键特征骨骼路径指纹。
        /// </summary>
        public virtual void BakeGenericSampleBonePaths()
        {
            _genericSampleBonePaths.Clear();
            var clip = AnimationClip;
            if (clip == null || clip.isHumanMotion) return;

            var bindings = UnityEditor.AnimationUtility.GetCurveBindings(clip);
            HashSet<string> seen = new HashSet<string>();
            for (int i = 0; i < bindings.Length; i++)
            {
                string path = bindings[i].path;
                if (!string.IsNullOrEmpty(path) && seen.Add(path))
                {
                    _genericSampleBonePaths.Add(path);
                    if (_genericSampleBonePaths.Count >= 3) break;
                }
            }
        }
#endif

        /// <summary>
        /// 深度克隆当前数据模型。
        /// </summary>
        public virtual MontageSequenceData Clone()
        {
            return new MontageSequenceData(this);
        }

        #endregion
    }
}
