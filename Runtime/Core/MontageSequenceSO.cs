using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 蒙太奇核心数据资产（ScriptableObject）。
    /// 基于单一 AnimationClip 配置动画播放属性、淡入淡出曲线、Root Motion 开关、去语义化物理分段与多轨道表现块。
    /// </summary>
    [CreateAssetMenu(fileName = "Montage_", menuName = "Cwc/Montage/Montage Sequence")]
    public class MontageSequenceSO : ScriptableObject
    {
        #region Inspector 字段

        [Header("Animation Base")]
        [Tooltip("核心动画片段。整个蒙太奇的时间轴长度与基准帧率由该片段决定。")]
        [SerializeField] private AnimationClip _animationClip;

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
        /// 核心动画片段。
        /// </summary>
        public AnimationClip AnimationClip
        {
            get => _animationClip;
            set => _animationClip = value;
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
        /// 动画总时长（秒）。
        /// </summary>
        public float TotalDuration => _animationClip != null ? _animationClip.length : 0.0f;

        /// <summary>
        /// 蒙太奇单次播放的权威自然终点时间戳（秒，考虑推迟淡出 BlendOutOffset）。
        /// </summary>
        public float NaturalEndTime => TotalDuration + Mathf.Max(0.0f, _blendOutOffset);

        /// <summary>
        /// 动画基准帧率。
        /// </summary>
        public float FrameRate => _animationClip != null ? _animationClip.frameRate : 30.0f;

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

            float total = TotalDuration;
            foreach (var t in timestamps)
            {
                if (t > 0.0001f && t < total - 0.0001f)
                {
                    _splitTimestamps.Add(t);
                }
            }

            _splitTimestamps.Sort();
        }

        #endregion
    }
}
