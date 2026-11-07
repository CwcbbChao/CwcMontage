using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 蒙太奇动画序列核心只读数据契约接口。
    /// 统一抽象了旧版 MontageSequenceSO、新版 MontageSequenceAsset 容器以及纯 C# 内存动态组装模型（MontageSequenceData）。
    /// </summary>
    public interface IMontageSequence
    {
        #region 时序与基准属性

        /// <summary>
        /// 蒙太奇在时间轴上的完整总物理时长（秒，涵盖所有动画切片与动作块）。
        /// </summary>
        float TotalDuration { get; }

        /// <summary>
        /// 蒙太奇单次播放自然结束时间点（秒，包含尾部自然淡出偏移量）。
        /// </summary>
        float NaturalEndTime { get; }

        #endregion

        #region 分层切片轨道

        /// <summary>
        /// 是否启用上半身混合分层轨道。
        /// </summary>
        bool EnableUpperBodyTrack { get; }

        /// <summary>
        /// 上半身轨道的基础混合权重 [0.0, 1.0]。
        /// </summary>
        float UpperBodyWeight { get; }

        /// <summary>
        /// 上半身轨道的动画切片只读列表。
        /// </summary>
        IReadOnlyList<MontageAnimationSegment> UpperBodySegments { get; }

        /// <summary>
        /// 是否开启上半身脊柱解耦朝向补偿。
        /// </summary>
        bool DecoupleUpperBodyOrientation { get; }

        /// <summary>
        /// 该蒙太奇专用的自定义上半身遮罩（若为空则使用控制器默认的人形遮罩）。
        /// </summary>
        AvatarMask CustomUpperBodyMask { get; }

        /// <summary>
        /// 全身核心主轨道的混合权重 [0.0, 1.0]。
        /// </summary>
        float FullBodyWeight { get; }

        /// <summary>
        /// 全身核心主轨道的动画切片只读列表。
        /// </summary>
        IReadOnlyList<MontageAnimationSegment> FullBodySegments { get; }

        /// <summary>
        /// 是否启用叠加（Additive）受击/后坐力轨道。
        /// </summary>
        bool EnableAdditiveTrack { get; }

        /// <summary>
        /// 叠加轨道的基础混合权重 [0.0, 1.0]。
        /// </summary>
        float AdditiveWeight { get; }

        /// <summary>
        /// 叠加轨道的动画切片只读列表。
        /// </summary>
        IReadOnlyList<MontageAnimationSegment> AdditiveSegments { get; }

        #endregion

        #region 骨骼特征与根运动

        /// <summary>
        /// 获取蒙太奇锚点动画的骨骼是否为人形（Humanoid）。
        /// 若无任何实体动画切片则返回 null，视为全兼容。
        /// </summary>
        bool? GetAnchorSkeletonIsHuman();

        /// <summary>
        /// 播放过程中是否启用足底 IK（Foot IK）。
        /// </summary>
        bool IsFootIK { get; }

        /// <summary>
        /// 是否应用水平平面（X/Z）根运动位移。
        /// </summary>
        bool ApplyHorizontalRootMotion { get; }

        /// <summary>
        /// 是否应用垂直（Y）根运动位移。
        /// </summary>
        bool ApplyVerticalRootMotion { get; }

        /// <summary>
        /// 是否应用根运动旋转朝向。
        /// </summary>
        bool ApplyRotationRootMotion { get; }

        /// <summary>
        /// 泛型动作特征骨骼相对路径抽样指纹（用于非人形特异骨骼的快速兼容比对）。
        /// </summary>
        IReadOnlyList<string> GenericSampleBonePaths { get; }

        #endregion

        #region 物理分段与动作块轨道

        /// <summary>
        /// 物理切分时间戳只读列表（秒）。N 个切分点将时间轴划分为 N + 1 个 Section。
        /// </summary>
        IReadOnlyList<float> SplitTimestamps { get; }

        /// <summary>
        /// 视听与游戏逻辑动作块事件轨道只读列表。
        /// </summary>
        IReadOnlyList<MontageTrackData> Tracks { get; }

        #endregion

        #region 运行时动作块与分段评估

        /// <summary>
        /// 烘焙好的运行时不可变动作块扁平列表（过滤静音轨道与禁用块，按起始时间排序）。
        /// </summary>
        IReadOnlyList<MontageActionBlockData> BakedRuntimeActionBlocks { get; }

        /// <summary>
        /// 物理分段总数量（N 个切分时间戳切分为 N + 1 个 Section）。
        /// </summary>
        int SectionCount { get; }

        /// <summary>
        /// 获取指定物理分段的时长（秒）。
        /// </summary>
        float GetSectionDuration(int sectionIndex);

        /// <summary>
        /// 获取指定物理分段的时间区间 [start, end]（秒）。
        /// </summary>
        (float start, float end) GetSectionRange(int sectionIndex);

        /// <summary>
        /// 根据绝对时间戳计算其归属的物理分段索引。
        /// </summary>
        int GetSectionIndexAtTime(float time);

        /// <summary>
        /// 评估指定通道上的所有切片在当帧的采样时间与平滑权重（0 GC 写入传入的列表缓存）。
        /// </summary>
        void EvaluateChannelSegments(
            MontageLayerChannel channel,
            float timelineTime,
            List<int> outIndices,
            List<float> outSampleTimes,
            List<float> outWeights);

        /// <summary>
        /// 评估全身主轨道切片在当帧的采样时间与权重（兼容旧版直接调用）。
        /// </summary>
        void EvaluateAnimationSegments(
            float timelineTime,
            List<int> outIndices,
            List<float> outSampleTimes,
            List<float> outWeights);

        #endregion

        #region 辅助查询方法

        /// <summary>
        /// 检查指定的图层通道上是否包含任何有效的动画切片。
        /// </summary>
        /// <param name="channel">目标混合通道（UpperBody / FullBody / Additive）</param>
        /// <returns>若存在至少一个有效切片则返回 true</returns>
        bool HasAnyAnimationInChannel(MontageLayerChannel channel);

        /// <summary>
        /// 获取指定图层通道的基础混合权重 [0.0, 1.0]。
        /// </summary>
        /// <param name="channel">目标通道</param>
        /// <returns>通道混合权重</returns>
        float GetChannelWeight(MontageLayerChannel channel);

        #endregion
    }

    /// <summary>
    /// 蒙太奇序列契约接口全局扩展辅助类。
    /// 提供统一空安全检测（规避 Unity 假 null 陷阱）与便捷查询。
    /// </summary>
    public static class MontageSequenceExtensions
    {
        /// <summary>
        /// 安全判定指定的蒙太奇序列是否有效存在。
        /// 正确兼容纯 C# 内存对象判空与 UnityEngine.Object 假 null（已 Destroy）拦截。
        /// </summary>
        /// <param name="sequence">待检测的序列实例</param>
        /// <returns>若存在且未被销毁则返回 true</returns>
        public static bool IsValid(this IMontageSequence sequence)
        {
            if (sequence == null) return false;

            // 若实现类为 UnityEngine.Object（如 ScriptableObject），走 Unity 底层重载的 bool 运算符
            if (sequence is UnityEngine.Object unityObj && !unityObj)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 查询指定的蒙太奇序列是否具备多阶段物理分段（分段总数大于 1）。
        /// 若为 false，通常代表单切片直接组装或无切分点的简易动作。
        /// </summary>
        /// <param name="sequence">待检测的序列实例</param>
        /// <returns>若分段总数大于 1 则返回 true</returns>
        public static bool HasSections(this IMontageSequence sequence)
        {
            return sequence != null && sequence.SectionCount > 1;
        }

        /// <summary>
        /// 查询指定的蒙太奇序列是否包含特定索引的物理分段。
        /// </summary>
        /// <param name="sequence">待检测的序列实例</param>
        /// <param name="sectionIndex">物理分段索引（基于 0）</param>
        /// <returns>若分段存在且在有效范围内则返回 true</returns>
        public static bool HasSection(this IMontageSequence sequence, int sectionIndex)
        {
            return sequence != null && sectionIndex >= 0 && sectionIndex < sequence.SectionCount;
        }

        /// <summary>
        /// 获取蒙太奇序列的首个有效动画切片（若无任何切片则返回 null）。
        /// </summary>
        public static AnimationClip GetPrimaryClip(this IMontageSequence sequence)
        {
            if (sequence == null) return null;

            if (sequence.FullBodySegments != null)
            {
                for (int i = 0; i < sequence.FullBodySegments.Count; i++)
                {
                    var seg = sequence.FullBodySegments[i];
                    if (seg != null && seg.Clip != null) return seg.Clip;
                }
            }

            if (sequence.UpperBodySegments != null)
            {
                for (int i = 0; i < sequence.UpperBodySegments.Count; i++)
                {
                    var seg = sequence.UpperBodySegments[i];
                    if (seg != null && seg.Clip != null) return seg.Clip;
                }
            }

            if (sequence.AdditiveSegments != null)
            {
                for (int i = 0; i < sequence.AdditiveSegments.Count; i++)
                {
                    var seg = sequence.AdditiveSegments[i];
                    if (seg != null && seg.Clip != null) return seg.Clip;
                }
            }

            return null;
        }
    }
}
