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

        [Header("Animation Base")]
        [Tooltip("单条动画轨道上的所有动画片段列表（支持多动画拼接、独立调速与交叉混合）。")]
        [SerializeField] private List<MontageAnimationSegment> _animationSegments = new();

        [Tooltip("默认播放图层索引（0 为基础层，1+ 为叠加/覆盖动作层）。")]
        [Min(0)]
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

        [Header("Physical Sections (去语义化物理分段)")]
        [Tooltip("时间轴切分点时间戳列表（秒）。按升序排列，N 个切分点将动画严格划分为 N + 1 个连续分段。")]
        [SerializeField] private List<float> _splitTimestamps = new();

        [Header("Tracks & Action Blocks")]
        [Tooltip("表现轨道列表。")]
        [SerializeField] private List<MontageTrackData> _tracks = new();

        #endregion

        #region 公共属性

        /// <summary>
        /// 单条动画轨道上的所有动画片段列表。
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

                // 1. 核心动画轨道上的动画片段
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
        /// 校验并对动画轨道上的片段列表按时间戳升序排序。
        /// </summary>
        public void SortAnimationSegments()
        {
            if (_animationSegments == null || _animationSegments.Count <= 1) return;
            _animationSegments.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
        }

        /// <summary>
        /// 校验所有动画片段的起止时间与时长合法性。
        /// </summary>
        public void EnsureSegmentsValid()
        {
            if (_animationSegments == null) return;

            for (int i = 0; i < _animationSegments.Count; i++)
            {
                _animationSegments[i]?.EnsureValid();
            }
        }

        /// <summary>
        /// 评估指定蒙太奇绝对时间戳下所有活跃片段的索引、内部采样时间与归一化混合权重。
        /// 支持相邻片段交叉过渡（Crossfade）的精确平滑插值。零 GC 内存分配。
        /// </summary>
        /// <param name="timelineTime">蒙太奇绝对时间戳（秒）</param>
        /// <param name="outIndices">输出活跃片段在列表中的索引</param>
        /// <param name="outSampleTimes">输出对应片段内部的采样时间戳（秒）</param>
        /// <param name="outWeights">输出对应片段的归一化混合权重 [0.0, 1.0]</param>
        public void EvaluateAnimationSegments(
            float timelineTime,
            List<int> outIndices,
            List<float> outSampleTimes,
            List<float> outWeights)
        {
            if (outIndices == null || outSampleTimes == null || outWeights == null) return;
            outIndices.Clear();
            outSampleTimes.Clear();
            outWeights.Clear();

            int count = _animationSegments != null ? _animationSegments.Count : 0;
            if (count == 0)
            {
                return;
            }

            // 单个片段快速路径
            if (count == 1)
            {
                var single = _animationSegments[0];
                if (single?.Clip != null)
                {
                    outIndices.Add(0);
                    outSampleTimes.Add(single.EvaluateLocalSampleTime(timelineTime));
                    outWeights.Add(1.0f);
                }
                return;
            }

            // 多片段重叠与交叉混音计算
            float t = Mathf.Max(0.0f, timelineTime);

            // 1. 查找所有时间覆盖当前时间点 t 的片段
            for (int i = 0; i < count; i++)
            {
                var seg = _animationSegments[i];
                if (seg?.Clip == null) continue;

                float start = seg.StartTime;
                float end = seg.EndTime;

                // 容差判定：处于片段区间内，或到达最后一个片段末端
                bool isInside = (t >= start && t < end) || (i == count - 1 && t >= end && Mathf.Abs(t - end) < 0.001f);
                if (isInside)
                {
                    outIndices.Add(i);
                }
            }

            // 2. 若当前落在两段片段之间的间隙或头部/尾部外
            if (outIndices.Count == 0)
            {
                // 落在第一个有效片段之前：保持首个片段的第 0 帧起首姿态
                if (t < _animationSegments[0].StartTime)
                {
                    outIndices.Add(0);
                    outSampleTimes.Add(_animationSegments[0].EvaluateLocalSampleTime(_animationSegments[0].StartTime));
                    outWeights.Add(1.0f);
                    return;
                }

                // 落在最后一个片段之后：保持末尾片段的末尾帧姿态
                int lastIdx = count - 1;
                if (t >= _animationSegments[lastIdx].EndTime)
                {
                    outIndices.Add(lastIdx);
                    outSampleTimes.Add(_animationSegments[lastIdx].EvaluateLocalSampleTime(_animationSegments[lastIdx].EndTime));
                    outWeights.Add(1.0f);
                    return;
                }

                // 落在两段动画之间的真空期（Gap）：
                // 严格停留在紧邻的前一片段的末尾帧姿态（Hold Last Frame），绝不跳变至后一片段首帧
                int preIdx = 0;
                for (int i = 0; i < count; i++)
                {
                    if (_animationSegments[i].EndTime <= t)
                    {
                        preIdx = i;
                    }
                    else
                    {
                        break;
                    }
                }

                var preSeg = _animationSegments[preIdx];
                outIndices.Add(preIdx);
                outSampleTimes.Add(preSeg.EvaluateLocalSampleTime(preSeg.EndTime));
                outWeights.Add(1.0f);
                return;
            }

            // 3. 只有一个活跃片段（无重叠）
            if (outIndices.Count == 1)
            {
                int idx = outIndices[0];
                var seg = _animationSegments[idx];
                outSampleTimes.Add(seg.EvaluateLocalSampleTime(t));
                outWeights.Add(1.0f);
                return;
            }

            // 4. 有两个相邻片段发生交叉重叠（Crossfade）
            if (outIndices.Count == 2)
            {
                int idxA = outIndices[0];
                int idxB = outIndices[1];
                var segA = _animationSegments[idxA];
                var segB = _animationSegments[idxB];

                // 严格保证 prevSeg 为起点较早者，nextSeg 为起点较晚者
                int prevIdx = segA.StartTime <= segB.StartTime ? idxA : idxB;
                int nextIdx = segA.StartTime <= segB.StartTime ? idxB : idxA;
                var prevSeg = _animationSegments[prevIdx];
                var nextSeg = _animationSegments[nextIdx];

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
                var seg = _animationSegments[outIndices[k]];
                outSampleTimes.Add(seg.EvaluateLocalSampleTime(t));
                outWeights.Add(uniformWeight);
            }
        }

        #endregion
    }
}
