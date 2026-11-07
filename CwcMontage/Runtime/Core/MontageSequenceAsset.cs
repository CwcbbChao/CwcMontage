using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 新版现代蒙太奇动画配置资产（ScriptableObject 包装容器）。
    /// 内部封装纯 C# 的 MontageSequenceData 数据模型，实现数据与资产容器彻底解耦。
    /// 完整实现 IMontageSequence 契约。
    /// </summary>
    [CreateAssetMenu(fileName = "MontageAsset_", menuName = "Cwc/Montage/Montage Sequence Asset")]
    public class MontageSequenceAsset : ScriptableObject, IMontageSequence
    {
        #region 1. 常量与静态成员

        #endregion

        #region 2. Inspector 序列化字段

        [Tooltip("纯 C# 蒙太奇核心数据配置。")]
        [SerializeField] private MontageSequenceData _data = new MontageSequenceData();

        #endregion

        #region 3. 受保护运行时字段

        #endregion

        #region 4. 公共属性与契约实现

        /// <summary>
        /// 核心纯 C# 数据模型引用。
        /// </summary>
        public virtual MontageSequenceData Data => _data;

        public virtual float TotalDuration => _data != null ? _data.TotalDuration : 0.0f;
        public virtual float NaturalEndTime => _data != null ? _data.NaturalEndTime : 0.0f;

        public virtual float FrameRate => _data != null ? _data.FrameRate : 30.0f;
        public virtual int TotalFrames => _data != null ? _data.TotalFrames : 30;

        public virtual bool EnableUpperBodyTrack { get => _data != null && _data.EnableUpperBodyTrack; set { if (_data != null) _data.EnableUpperBodyTrack = value; } }
        public virtual bool EnableUpperBody { get => _data != null && _data.EnableUpperBody; set { if (_data != null) _data.EnableUpperBody = value; } }
        public virtual float UpperBodyWeight { get => _data != null ? _data.UpperBodyWeight : 1.0f; set { if (_data != null) _data.UpperBodyWeight = value; } }
        public virtual List<MontageAnimationSegment> UpperBodySegments => _data?.UpperBodySegments;
        IReadOnlyList<MontageAnimationSegment> IMontageSequence.UpperBodySegments => _data?.UpperBodySegments;

        public virtual bool DecoupleUpperBodyOrientation { get => _data != null && _data.DecoupleUpperBodyOrientation; set { if (_data != null) _data.DecoupleUpperBodyOrientation = value; } }
        public virtual AvatarMask CustomUpperBodyMask { get => _data != null ? _data.CustomUpperBodyMask : null; set { if (_data != null) _data.CustomUpperBodyMask = value; } }

        public virtual float FullBodyWeight { get => _data != null ? _data.FullBodyWeight : 1.0f; set { if (_data != null) _data.FullBodyWeight = value; } }
        public virtual List<MontageAnimationSegment> FullBodySegments => _data?.FullBodySegments;
        public virtual List<MontageAnimationSegment> AnimationSegments => FullBodySegments;
        IReadOnlyList<MontageAnimationSegment> IMontageSequence.FullBodySegments => _data?.FullBodySegments;

        public virtual bool EnableAdditiveTrack { get => _data != null && _data.EnableAdditiveTrack; set { if (_data != null) _data.EnableAdditiveTrack = value; } }
        public virtual bool EnableAdditive { get => _data != null && _data.EnableAdditive; set { if (_data != null) _data.EnableAdditive = value; } }
        public virtual float AdditiveWeight { get => _data != null ? _data.AdditiveWeight : 1.0f; set { if (_data != null) _data.AdditiveWeight = value; } }
        public virtual List<MontageAnimationSegment> AdditiveSegments => _data?.AdditiveSegments;
        IReadOnlyList<MontageAnimationSegment> IMontageSequence.AdditiveSegments => _data?.AdditiveSegments;

#if UNITY_EDITOR
        public virtual bool OverridePreviewModel { get => _data != null && _data.OverridePreviewModel; set { if (_data != null) _data.OverridePreviewModel = value; } }
        public virtual GameObject PreviewModel { get => _data != null ? _data.PreviewModel : null; set { if (_data != null) _data.PreviewModel = value; } }
#endif

        public virtual bool IsFootIK { get => _data != null && _data.IsFootIK; set { if (_data != null) _data.IsFootIK = value; } }
        public virtual bool ApplyHorizontalRootMotion { get => _data != null && _data.ApplyHorizontalRootMotion; set { if (_data != null) _data.ApplyHorizontalRootMotion = value; } }
        public virtual bool ApplyVerticalRootMotion { get => _data != null && _data.ApplyVerticalRootMotion; set { if (_data != null) _data.ApplyVerticalRootMotion = value; } }
        public virtual bool ApplyRotationRootMotion { get => _data != null && _data.ApplyRotationRootMotion; set { if (_data != null) _data.ApplyRotationRootMotion = value; } }

        public virtual List<string> GenericSampleBonePaths => _data?.GenericSampleBonePaths;
        IReadOnlyList<string> IMontageSequence.GenericSampleBonePaths => _data?.GenericSampleBonePaths;

        public virtual List<float> SplitTimestamps => _data?.SplitTimestamps;
        IReadOnlyList<float> IMontageSequence.SplitTimestamps => _data?.SplitTimestamps;

        public virtual List<MontageTrackData> Tracks => _data?.Tracks;
        IReadOnlyList<MontageTrackData> IMontageSequence.Tracks => _data?.Tracks;

        public virtual IReadOnlyList<MontageActionBlockData> BakedRuntimeActionBlocks => _data != null ? _data.BakedRuntimeActionBlocks : Array.Empty<MontageActionBlockData>();

        #endregion

        #region 5. Unity 生命周期

        protected virtual void OnEnable()
        {
            if (_data == null)
            {
                _data = new MontageSequenceData();
            }
            InvalidateBakedBlocks();
        }

        protected virtual void OnValidate()
        {
            EnsureSegmentsValid();
            ValidateActionBlocks();
            InvalidateBakedBlocks();
#if UNITY_EDITOR
            BakeGenericSampleBonePaths();
#endif
        }

        #endregion

        #region 6. 公共操作与契约评估方法

        public virtual int SectionCount => _data != null ? _data.SectionCount : 1;

        public virtual float GetSectionDuration(int sectionIndex)
        {
            return _data != null ? _data.GetSectionDuration(sectionIndex) : 0f;
        }

        public virtual (float start, float end) GetSectionRange(int sectionIndex)
        {
            return _data != null ? _data.GetSectionRange(sectionIndex) : (0f, 0f);
        }

        public virtual int GetSectionIndexAtTime(float time)
        {
            return _data != null ? _data.GetSectionIndexAtTime(time) : 0;
        }

        public virtual void EvaluateAnimationSegments(
            float timelineTime,
            List<int> outIndices,
            List<float> outSampleTimes,
            List<float> outWeights)
        {
            _data?.EvaluateAnimationSegments(timelineTime, outIndices, outSampleTimes, outWeights);
        }

        public virtual void EvaluateChannelSegments(
            MontageLayerChannel channel,
            float timelineTime,
            List<int> outIndices,
            List<float> outSampleTimes,
            List<float> outWeights)
        {
            _data?.EvaluateChannelSegments(channel, timelineTime, outIndices, outSampleTimes, outWeights);
        }

        public virtual bool? GetAnchorSkeletonIsHuman()
        {
            return _data != null ? _data.GetAnchorSkeletonIsHuman() : null;
        }

        public virtual bool HasAnyAnimationInChannel(MontageLayerChannel channel)
        {
            return _data != null && _data.HasAnyAnimationInChannel(channel);
        }

        public virtual float GetChannelWeight(MontageLayerChannel channel)
        {
            return _data != null ? _data.GetChannelWeight(channel) : 1.0f;
        }

        public virtual void SetChannelWeight(MontageLayerChannel channel, float weight)
        {
            _data?.SetChannelWeight(channel, weight);
        }

        public virtual void SetChannelEnabled(MontageLayerChannel channel, bool enabled)
        {
            _data?.SetChannelEnabled(channel, enabled);
        }

        public virtual bool IsChannelEnabled(MontageLayerChannel channel)
        {
            return _data != null && _data.IsChannelEnabled(channel);
        }

        public virtual List<MontageAnimationSegment> GetSegments(MontageLayerChannel channel)
        {
            return _data?.GetSegments(channel);
        }

        public virtual bool MoveSegmentChannel(MontageAnimationSegment segment, MontageLayerChannel fromChannel, MontageLayerChannel toChannel)
        {
            return _data != null && _data.MoveSegmentChannel(segment, fromChannel, toChannel);
        }

        public virtual void SetSplitTimestamps(IEnumerable<float> timestamps)
        {
            _data?.SetSplitTimestamps(timestamps);
        }

        public virtual void SortAnimationSegments()
        {
            _data?.SortAnimationSegments();
        }

        public virtual void SortAllChannelSegments()
        {
            _data?.SortAllChannelSegments();
        }

        public virtual void SortChannelSegments(MontageLayerChannel channel)
        {
            _data?.SortChannelSegments(channel);
        }

        public virtual void EnsureSegmentsValid()
        {
            _data?.EnsureSegmentsValid();
        }

        public virtual void ValidateActionBlocks()
        {
            _data?.ValidateActionBlocks();
        }

        public virtual void InvalidateBakedBlocks()
        {
            _data?.InvalidateBakedBlocks();
        }

#if UNITY_EDITOR
        public virtual void BakeGenericSampleBonePaths()
        {
            _data?.BakeGenericSampleBonePaths();
        }
#endif

        /// <summary>
        /// 隐式转换运算符：方便直接当做纯数据 MontageSequenceData 使用。
        /// </summary>
        public static implicit operator MontageSequenceData(MontageSequenceAsset asset)
        {
            return asset != null ? asset.Data : null;
        }

        #endregion
    }
}
