using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 蒙太奇核心数据资产（ScriptableObject）。
    /// 基于多片段动画轨道（MontageAnimationSegment）配置动画播放属性、淡入淡出曲线、Root Motion 开关、去语义化物理分段与多轨道表现块。
    /// </summary>
    [CreateAssetMenu(fileName = "Montage_", menuName = "Cwc/Montage/Montage Sequence")]
    public class MontageSequenceSO : ScriptableObject
    {
        #region Inspector 字段

        [Tooltip("是否激活上半身动画轨道（配合角色移动）。")]
        [SerializeField] private bool _enableUpperBodyTrack;

        [Tooltip("上半身动画层整体权重（0.0 ~ 1.0，用于控制上半身动作融合强弱）。")]
        [Range(0f, 1f)]
        [SerializeField] private float _upperBodyWeight = 1.0f;

        [Tooltip("上半身动画片段列表。")]
        [SerializeField] private List<MontageAnimationSegment> _upperBodySegments = new();

        [Tooltip("全身核心动画层整体权重（0.0 ~ 1.0，主层默认 1.0）。")]
        [Range(0f, 1f)]
        [SerializeField] private float _fullBodyWeight = 1.0f;

        [Tooltip("全身核心动画片段列表（默认常开主轨道，大幅度转身/大招/翻滚）。")]
        [SerializeField] private List<MontageAnimationSegment> _animationSegments = new();

        [Tooltip("是否激活受击/抖动叠加动画轨道。")]
        [SerializeField] private bool _enableAdditiveTrack;

        [Tooltip("叠加动画层整体权重（0.0 ~ 1.0，用于控制受击、抖动等叠加姿态的强弱）。")]
        [Range(0f, 1f)]
        [SerializeField] private float _additiveWeight = 1.0f;

        [Tooltip("叠加动画片段列表。")]
        [SerializeField] private List<MontageAnimationSegment> _additiveSegments = new();

        [Tooltip("是否开启上半身基准骨骼相对根节点姿态解耦（消除下半身奔跑前倾与晃动，100% 还原源动画中侧身劈砍、斜斩等真实体态）。")]
        [SerializeField] private bool _decoupleUpperBodyOrientation = true;

        [HideInInspector]
        [SerializeField] private int _animationLayer = 0;

        [Tooltip("基础播放速率倍率（默认 1.0）。")]
        [Min(0.01f)]
        [SerializeField] private float _basePlayRate = 1.0f;

        [Tooltip("整段动画是否默认循环播放。")]
        [SerializeField] private bool _isLooping;

        [Tooltip("是否在此蒙太奇播放期间启用脚部反向动力学 (Foot IK)。")]
        [SerializeField] private bool _isFootIK;

        [Header("Blending Settings")]
        [Tooltip("默认淡入过渡时长（秒）。")]
        [Min(0.0f)]
        [SerializeField] private float _defaultBlendInTime = 0.15f;

        [Tooltip("淡入插值曲线。")]
        [SerializeField] private AnimationCurve _blendInCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("默认淡出过渡时长（秒）。")]
        [Min(0.0f)]
        [SerializeField] private float _defaultBlendOutTime = 0.15f;

        [Tooltip("淡出插值曲线。")]
        [SerializeField] private AnimationCurve _blendOutCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("淡出触发时间偏移量（秒，用于让动画在自然结束前提前触发淡出）。")]
        [SerializeField] private float _blendOutOffset;

        [Header("Root Motion Settings")]
        [Tooltip("是否应用水平轴（X/Z 轴）的根运动位移。")]
        [SerializeField] private bool _applyHorizontalRootMotion;

        [Tooltip("是否应用垂直轴（Y 轴）的根运动位移。")]
        [SerializeField] private bool _applyVerticalRootMotion;

        [Tooltip("是否应用根运动旋转。")]
        [SerializeField] private bool _applyRotationRootMotion;

        [Header("Physical Sections")]
        [Tooltip("时间轴切分点时间戳列表（秒）。按升序排列，N 个切分点将动画严格划分为 N + 1 个连续分段。")]
        [SerializeField] private List<float> _splitTimestamps = new();

        [Header("Tracks & Action Blocks")]
        [Tooltip("表现轨道列表。")]
        [SerializeField] private List<MontageTrackData> _tracks = new();

        #endregion

        #region 公共属性

        /// <summary>
        /// 是否激活上半身动画轨道。
        /// </summary>
        public bool EnableUpperBodyTrack
        {
            get => _enableUpperBodyTrack;
            set => _enableUpperBodyTrack = value;
        }

        /// <summary>
        /// 上半身轨道快捷别名。
        /// </summary>
        public bool EnableUpperBody
        {
            get => _enableUpperBodyTrack;
            set => _enableUpperBodyTrack = value;
        }

        /// <summary>
        /// 是否开启上半身基准骨骼相对根节点姿态解耦。
        /// </summary>
        public bool DecoupleUpperBodyOrientation
        {
            get => _decoupleUpperBodyOrientation;
            set => _decoupleUpperBodyOrientation = value;
        }

        /// <summary>
        /// 上半身动画片段列表。
        /// </summary>
        public List<MontageAnimationSegment> UpperBodySegments => _upperBodySegments;

        /// <summary>
        /// 上半身动画层整体权重 [0.0, 1.0]。
        /// </summary>
        public float UpperBodyWeight
        {
            get => _upperBodyWeight;
            set => _upperBodyWeight = Mathf.Clamp01(value);
        }

        /// <summary>
        /// 全身核心动画片段列表（主轨道）。
        /// </summary>
        public List<MontageAnimationSegment> FullBodySegments => _animationSegments;

        /// <summary>
        /// 全身核心动画层整体权重 [0.0, 1.0]。
        /// </summary>
        public float FullBodyWeight
        {
            get => _fullBodyWeight;
            set => _fullBodyWeight = Mathf.Clamp01(value);
        }

        /// <summary>
        /// 是否激活叠加动画轨道。
        /// </summary>
        public bool EnableAdditiveTrack
        {
            get => _enableAdditiveTrack;
            set => _enableAdditiveTrack = value;
        }

        /// <summary>
        /// 叠加轨道快捷别名。
        /// </summary>
        public bool EnableAdditive
        {
            get => _enableAdditiveTrack;
            set => _enableAdditiveTrack = value;
        }

        /// <summary>
        /// 叠加动画片段列表。
        /// </summary>
        public List<MontageAnimationSegment> AdditiveSegments => _additiveSegments;

        /// <summary>
        /// 叠加动画层整体权重 [0.0, 1.0]。
        /// </summary>
        public float AdditiveWeight
        {
            get => _additiveWeight;
            set => _additiveWeight = Mathf.Clamp01(value);
        }

        /// <summary>
        /// 全身动画片段列表（向后兼容保留）。
        /// </summary>
        public List<MontageAnimationSegment> AnimationSegments => _animationSegments;

        /// <summary>
        /// 核心首个动画片段（只读便捷属性，指向第一个有效 Segment 的 Clip）。
        /// </summary>
        public AnimationClip AnimationClip
        {
            get
            {
                if (_animationSegments != null)
                {
                    for (int i = 0; i < _animationSegments.Count; i++)
                    {
                        if (_animationSegments[i]?.Clip != null)
                        {
                            return _animationSegments[i].Clip;
                        }
                    }
                }
                return null;
            }
        }

        /// <summary>
        /// 默认播放图层。
        /// </summary>
        public int AnimationLayer
        {
            get => _animationLayer;
            set => _animationLayer = Mathf.Max(0, value);
        }

        /// <summary>
        /// 基础播放速率。
        /// </summary>
        public float BasePlayRate
        {
            get => _basePlayRate;
            set => _basePlayRate = Mathf.Max(0.01f, value);
        }

        /// <summary>
        /// 是否循环。
        /// </summary>
        public bool IsLooping
        {
            get => _isLooping;
            set => _isLooping = value;
        }

        /// <summary>
        /// 是否启用脚部 IK。
        /// </summary>
        public bool IsFootIK
        {
            get => _isFootIK;
            set => _isFootIK = value;
        }

        /// <summary>
        /// 默认淡入时长（秒）。
        /// </summary>
        public float DefaultBlendInTime => _defaultBlendInTime;

        /// <summary>
        /// 淡入插值曲线。
        /// </summary>
        public AnimationCurve BlendInCurve => _blendInCurve;

        /// <summary>
        /// 默认淡出时长（秒）。
        /// </summary>
        public float DefaultBlendOutTime => _defaultBlendOutTime;

        /// <summary>
        /// 淡出插值曲线。
        /// </summary>
        public AnimationCurve BlendOutCurve => _blendOutCurve;

        /// <summary>
        /// 淡出偏移量（秒）。
        /// </summary>
        public float BlendOutOffset => _blendOutOffset;

        /// <summary>
        /// 是否应用水平根运动。
        /// </summary>
        public bool ApplyHorizontalRootMotion => _applyHorizontalRootMotion;

        /// <summary>
        /// 是否应用垂直根运动。
        /// </summary>
        public bool ApplyVerticalRootMotion => _applyVerticalRootMotion;

        /// <summary>
        /// 是否应用旋转根运动。
        /// </summary>
        public bool ApplyRotationRootMotion => _applyRotationRootMotion;

        /// <summary>
        /// 动画总时长（秒，取所有动画片段、表现轨道动作块与切分点的最大结束时间）。
        /// </summary>
        public float TotalDuration
        {
            get
            {
                float maxEndTime = 0.0f;

                // 1. 核心动画轨道上的动画片段 (FullBody、UpperBody、Additive)
                if (_animationSegments != null)
                {
                    for (int i = 0; i < _animationSegments.Count; i++)
                    {
                        var seg = _animationSegments[i];
                        if (seg != null && seg.EndTime > maxEndTime)
                        {
                            maxEndTime = seg.EndTime;
                        }
                    }
                }

                if (_enableUpperBodyTrack && _upperBodySegments != null)
                {
                    for (int i = 0; i < _upperBodySegments.Count; i++)
                    {
                        var seg = _upperBodySegments[i];
                        if (seg != null && seg.EndTime > maxEndTime)
                        {
                            maxEndTime = seg.EndTime;
                        }
                    }
                }

                if (_enableAdditiveTrack && _additiveSegments != null)
                {
                    for (int i = 0; i < _additiveSegments.Count; i++)
                    {
                        var seg = _additiveSegments[i];
                        if (seg != null && seg.EndTime > maxEndTime)
                        {
                            maxEndTime = seg.EndTime;
                        }
                    }
                }

                // 2. 表现轨道上的动作块
                if (_tracks != null)
                {
                    for (int i = 0; i < _tracks.Count; i++)
                    {
                        var track = _tracks[i];
                        if (track?.ActionBlocks == null) continue;
                        for (int j = 0; j < track.ActionBlocks.Count; j++)
                        {
                            var block = track.ActionBlocks[j];
                            if (block != null && block.EndTime > maxEndTime)
                            {
                                maxEndTime = block.EndTime;
                            }
                        }
                    }
                }

                // 3. 物理分段切分点
                if (_splitTimestamps != null)
                {
                    for (int i = 0; i < _splitTimestamps.Count; i++)
                    {
                        if (_splitTimestamps[i] > maxEndTime)
                        {
                            maxEndTime = _splitTimestamps[i];
                        }
                    }
                }

                return maxEndTime;
            }
        }

        /// <summary>
        /// 蒙太奇单次播放的权威自然终点时间戳（秒，考虑推迟淡出 BlendOutOffset）。
        /// </summary>
        public float NaturalEndTime => TotalDuration + Mathf.Max(0.0f, _blendOutOffset);

        /// <summary>
        /// 动画基准帧率（优先读取首个有效片段的采样帧率）。
        /// </summary>
        public float FrameRate
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
        /// 动画总帧数。
        /// </summary>
        public int TotalFrames => Mathf.RoundToInt(TotalDuration * FrameRate);

        /// <summary>
        /// 物理分段总数量（恒等于 切分点数量 + 1）。
        /// </summary>
        public int SectionCount => _splitTimestamps != null ? _splitTimestamps.Count + 1 : 1;

        /// <summary>
        /// 切分点时间戳只读列表。
        /// </summary>
        public IReadOnlyList<float> SplitTimestamps => _splitTimestamps;

        /// <summary>
        /// 表现轨道列表。
        /// </summary>
        public List<MontageTrackData> Tracks => _tracks;

        #endregion

        #region Unity 生命周期

        private void OnValidate()
        {
            EnsureSegmentsValid();
            ValidateActionBlocks();
        }

        #endregion

        #region 公共方法 (分段与物理时间计算)

        /// <summary>
        /// 校验并修正所有轨道上的动作块，强制保证其起止帧与时长合法（至少 1 帧）。
        /// </summary>
        public void ValidateActionBlocks()
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

        /// <summary>
        /// 获取指定物理分段的原始物理持续时间（秒）。
        /// </summary>
        /// <param name="sectionIndex">分段索引（从 0 开始）</param>
        /// <returns>分段持续秒数</returns>
        public float GetSectionDuration(int sectionIndex)
        {
            var range = GetSectionRange(sectionIndex);
            return Mathf.Max(0f, range.end - range.start);
        }

        /// <summary>
        /// 获取指定分段在时间轴上的起始与结束时间区间 [start, end]。
        /// </summary>
        /// <param name="sectionIndex">分段索引（从 0 开始）</param>
        /// <returns>包含 start 与 end 的时间元组</returns>
        public (float start, float end) GetSectionRange(int sectionIndex)
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

        /// <summary>
        /// 根据绝对时间戳查询其所在的物理分段索引。
        /// </summary>
        /// <param name="time">时间戳（秒）</param>
        /// <returns>分段索引（从 0 开始）</returns>
        public int GetSectionIndexAtTime(float time)
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

        /// <summary>
        /// 将所有分段的物理时长填充到外部列表中，零垃圾分配。
        /// </summary>
        /// <param name="outputList">接收分段时长的列表</param>
        public void GetSectionDurations(List<float> outputList)
        {
            if (outputList == null) return;
            outputList.Clear();

            int sections = SectionCount;
            for (int i = 0; i < sections; i++)
            {
                outputList.Add(GetSectionDuration(i));
            }
        }

        /// <summary>
        /// 批量更新切分点时间戳并自动完成升序排序与有效性剔除。
        /// </summary>
        /// <param name="timestamps">时间戳枚举</param>
        public void SetSplitTimestamps(IEnumerable<float> timestamps)
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
        /// 校验并对所有动画轨道上的片段列表按时间戳升序排序。
        /// </summary>
        public void SortAnimationSegments()
        {
            SortSegmentsList(_animationSegments);
            SortSegmentsList(_upperBodySegments);
            SortSegmentsList(_additiveSegments);
        }

        /// <summary>
        /// 校验并对所有通道动画轨道片段按时间戳升序排序。
        /// </summary>
        public void SortAllChannelSegments() => SortAnimationSegments();

        /// <summary>
        /// 对指定通道上的动画片段按时间戳升序排序。
        /// </summary>
        public void SortChannelSegments(MontageLayerChannel channel)
        {
            SortSegmentsList(GetSegments(channel));
        }

        private static void SortSegmentsList(List<MontageAnimationSegment> list)
        {
            if (list == null || list.Count <= 1) return;
            list.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
        }

        /// <summary>
        /// 校验所有动画片段的起止时间与时长合法性。
        /// </summary>
        public void EnsureSegmentsValid()
        {
            ValidateSegmentsList(_animationSegments);
            ValidateSegmentsList(_upperBodySegments);
            ValidateSegmentsList(_additiveSegments);
        }

        private static void ValidateSegmentsList(List<MontageAnimationSegment> list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                list[i]?.EnsureValid();
            }
        }

        /// <summary>
        /// 获取指定通道的动画片段列表。
        /// </summary>
        public List<MontageAnimationSegment> GetSegments(MontageLayerChannel channel)
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
        /// 查询指定通道是否处于激活开启状态。
        /// </summary>
        public bool IsChannelEnabled(MontageLayerChannel channel)
        {
            return channel switch
            {
                MontageLayerChannel.UpperBody => _enableUpperBodyTrack,
                MontageLayerChannel.FullBody => true, // 全身轨道恒定开启
                MontageLayerChannel.Additive => _enableAdditiveTrack,
                _ => true
            };
        }

        /// <summary>
        /// 设置指定通道的激活开启状态。
        /// </summary>
        public void SetChannelEnabled(MontageLayerChannel channel, bool enabled)
        {
            switch (channel)
            {
                case MontageLayerChannel.UpperBody:
                    _enableUpperBodyTrack = enabled;
                    break;
                case MontageLayerChannel.FullBody:
                    // 全身轨道恒定开启，不允许关闭
                    break;
                case MontageLayerChannel.Additive:
                    _enableAdditiveTrack = enabled;
                    break;
            }
        }

        /// <summary>
        /// 获取指定通道动画层的整体混合权重 [0.0, 1.0]。
        /// </summary>
        public float GetChannelWeight(MontageLayerChannel channel)
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
        /// 设置指定通道动画层的整体混合权重 [0.0, 1.0]。
        /// </summary>
        public void SetChannelWeight(MontageLayerChannel channel, float weight)
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
        /// 将动画片段从源通道迁移至目标通道。
        /// </summary>
        public bool MoveSegmentChannel(MontageAnimationSegment segment, MontageLayerChannel fromChannel, MontageLayerChannel toChannel)
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
        /// 查询指定通道当前是否包含任何有效动画片段。
        /// </summary>
        public bool HasAnyAnimationInChannel(MontageLayerChannel channel)
        {
            if (!IsChannelEnabled(channel)) return false;
            var list = GetSegments(channel);
            if (list == null || list.Count == 0) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i]?.Clip != null) return true;
            }
            return false;
        }

        /// <summary>
        /// 评估指定动画轨道在当前绝对时间戳下的活跃片段、采样时间与归一化混合权重。
        /// 【核心设计】：在动画空白区（Gap）干脆不播放（输出空列表，权重自然归 0），绝不僵死锁定第一帧或最后一帧！
        /// </summary>
        public static void EvaluateSegments(
            List<MontageAnimationSegment> segments,
            float timelineTime,
            List<int> outIndices,
            List<float> outSampleTimes,
            List<float> outWeights)
        {
            if (outIndices == null || outSampleTimes == null || outWeights == null) return;
            outIndices.Clear();
            outSampleTimes.Clear();
            outWeights.Clear();

            int count = segments != null ? segments.Count : 0;
            if (count == 0)
            {
                return;
            }

            float t = Mathf.Max(0.0f, timelineTime);

            // 1. 查找所有时间覆盖当前时间点 t 的片段
            for (int i = 0; i < count; i++)
            {
                var seg = segments[i];
                if (seg?.Clip == null) continue;

                float start = seg.StartTime;
                float end = seg.EndTime;

                // 容差判定：严格处于片段区间内 [start, end)
                bool isInside = (t >= start && t < end) || (i == count - 1 && t >= end && Mathf.Abs(t - end) < 0.001f);
                if (isInside)
                {
                    outIndices.Add(i);
                }
            }

            // 2. 若当前落在片段之外或两段片段之间的空白缝隙（Gap）
            if (outIndices.Count == 0)
            {
                // 空白区域不输出任何动画姿态，由底层自然接管，权重彻底归 0
                return;
            }

            // 3. 只有一个活跃片段（无重叠）
            if (outIndices.Count == 1)
            {
                int idx = outIndices[0];
                var seg = segments[idx];
                bool hasPrev = HasContinuousPreviousSegment(segments, idx);
                bool hasNext = HasContinuousNextSegment(segments, idx);

                float inFactor = hasPrev ? 1.0f : seg.CalculateFadeInFactor(t);
                float outFactor = hasNext ? 1.0f : seg.CalculateFadeOutFactor(t);
                float weight = Mathf.Min(inFactor, outFactor);

                outSampleTimes.Add(seg.EvaluateLocalSampleTime(t));
                outWeights.Add(weight);
                return;
            }

            // 4. 有两个相邻片段发生交叉重叠（Crossfade）
            if (outIndices.Count == 2)
            {
                int idxA = outIndices[0];
                int idxB = outIndices[1];
                var segA = segments[idxA];
                var segB = segments[idxB];

                int prevIdx = segA.StartTime <= segB.StartTime ? idxA : idxB;
                int nextIdx = segA.StartTime <= segB.StartTime ? idxB : idxA;
                var prevSeg = segments[prevIdx];
                var nextSeg = segments[nextIdx];

                float overlapStart = nextSeg.StartTime;
                float overlapEnd = Mathf.Min(prevSeg.EndTime, nextSeg.EndTime);
                float overlapDuration = Mathf.Max(0.0001f, overlapEnd - overlapStart);

                float progress = Mathf.Clamp01((t - overlapStart) / overlapDuration);
                float nextWeight = nextSeg.BlendCurve != null ? Mathf.Clamp01(nextSeg.BlendCurve.Evaluate(progress)) : progress;
                float prevWeight = Mathf.Clamp01(1.0f - nextWeight);

                float totalW = prevWeight + nextWeight;
                if (totalW > 0.0001f)
                {
                    prevWeight /= totalW;
                    nextWeight /= totalW;
                }
                else
                {
                    prevWeight = 0.5f;
                    nextWeight = 0.5f;
                }

                // 连续性判定：
                // prevSeg 在交叉区是过渡给 nextSeg，因此绝不触发自身的出点淡出；
                // nextSeg 在交叉区是由 prevSeg 接管而来，因此绝不触发自身的入点淡入；
                // 仅当整段重叠链条本身处于起止边缘时，才考虑外部边缘淡入淡出：
                bool prevHasPrev = HasContinuousPreviousSegment(segments, prevIdx);
                bool nextHasNext = HasContinuousNextSegment(segments, nextIdx);

                float chainInFactor = prevHasPrev ? 1.0f : prevSeg.CalculateFadeInFactor(t);
                float chainOutFactor = nextHasNext ? 1.0f : nextSeg.CalculateFadeOutFactor(t);
                float chainEdgeFactor = Mathf.Min(chainInFactor, chainOutFactor);

                prevWeight *= chainEdgeFactor;
                nextWeight *= chainEdgeFactor;

                outIndices[0] = prevIdx;
                outIndices[1] = nextIdx;

                outSampleTimes.Add(prevSeg.EvaluateLocalSampleTime(t));
                outWeights.Add(prevWeight);

                outSampleTimes.Add(nextSeg.EvaluateLocalSampleTime(t));
                outWeights.Add(nextWeight);
                return;
            }

            // 5. 极罕见的 3 个以上片段重叠保底：平分权重
            float uniformWeight = 1.0f / outIndices.Count;
            for (int k = 0; k < outIndices.Count; k++)
            {
                var seg = segments[outIndices[k]];
                outSampleTimes.Add(seg.EvaluateLocalSampleTime(t));
                outWeights.Add(uniformWeight);
            }
        }

        private static bool HasContinuousPreviousSegment(List<MontageAnimationSegment> segments, int index)
        {
            if (segments == null || index < 0 || index >= segments.Count) return false;
            var cur = segments[index];
            if (cur?.Clip == null) return false;

            for (int i = 0; i < segments.Count; i++)
            {
                if (i == index) continue;
                var other = segments[i];
                if (other?.Clip == null) continue;
                if (other.StartTime < cur.StartTime && other.EndTime >= cur.StartTime - 0.001f)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasContinuousNextSegment(List<MontageAnimationSegment> segments, int index)
        {
            if (segments == null || index < 0 || index >= segments.Count) return false;
            var cur = segments[index];
            if (cur?.Clip == null) return false;

            for (int i = 0; i < segments.Count; i++)
            {
                if (i == index) continue;
                var other = segments[i];
                if (other?.Clip == null) continue;
                if (other.EndTime > cur.EndTime && other.StartTime <= cur.EndTime + 0.001f)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 评估全身主干动画片段（向后兼容主接口）。
        /// </summary>
        public void EvaluateAnimationSegments(
            float timelineTime,
            List<int> outIndices,
            List<float> outSampleTimes,
            List<float> outWeights)
        {
            EvaluateSegments(_animationSegments, timelineTime, outIndices, outSampleTimes, outWeights);
        }

        /// <summary>
        /// 评估上半身动画片段。
        /// </summary>
        public void EvaluateUpperBodySegments(
            float timelineTime,
            List<int> outIndices,
            List<float> outSampleTimes,
            List<float> outWeights)
        {
            if (!_enableUpperBodyTrack)
            {
                outIndices?.Clear();
                outSampleTimes?.Clear();
                outWeights?.Clear();
                return;
            }
            EvaluateSegments(_upperBodySegments, timelineTime, outIndices, outSampleTimes, outWeights);
        }

        /// <summary>
        /// 评估叠加动画片段。
        /// </summary>
        public void EvaluateAdditiveSegments(
            float timelineTime,
            List<int> outIndices,
            List<float> outSampleTimes,
            List<float> outWeights)
        {
            if (!_enableAdditiveTrack)
            {
                outIndices?.Clear();
                outSampleTimes?.Clear();
                outWeights?.Clear();
                return;
            }
            EvaluateSegments(_additiveSegments, timelineTime, outIndices, outSampleTimes, outWeights);
        }

        /// <summary>
        /// 评估指定通道的动画片段。
        /// </summary>
        public void EvaluateChannelSegments(
            MontageLayerChannel channel,
            float timelineTime,
            List<int> outIndices,
            List<float> outSampleTimes,
            List<float> outWeights)
        {
            switch (channel)
            {
                case MontageLayerChannel.UpperBody:
                    EvaluateUpperBodySegments(timelineTime, outIndices, outSampleTimes, outWeights);
                    break;
                case MontageLayerChannel.FullBody:
                    EvaluateAnimationSegments(timelineTime, outIndices, outSampleTimes, outWeights);
                    break;
                case MontageLayerChannel.Additive:
                    EvaluateAdditiveSegments(timelineTime, outIndices, outSampleTimes, outWeights);
                    break;
            }
        }

        #endregion
    }
}
