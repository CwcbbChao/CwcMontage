using System;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 蒙太奇人形动画分层通道。
    /// 严格适配 Humanoid 标准骨骼，层级优先级自底向上依次递增：
    /// UpperBody (配合移动) -> FullBody (全身霸权独占) -> Additive (叠加微调)。
    /// </summary>
    public enum MontageLayerChannel
    {
        /// <summary>
        /// 上半身覆盖层（配合底层 Locomotion 移动，使用标准人形上半身骨骼遮罩）。
        /// 物理层 Index: 1
        /// </summary>
        UpperBody = 0,

        /// <summary>
        /// 全身独占层（大幅度转身、翻滚、大招、处决等，无遮罩，霸权覆盖下方图层）。
        /// 物理层 Index: 2
        /// </summary>
        FullBody = 1,

        /// <summary>
        /// 叠加层（受击颤抖、开火后坐力等，Additive 数学差值叠加）。
        /// 物理层 Index: 3
        /// </summary>
        Additive = 2
    }
}
