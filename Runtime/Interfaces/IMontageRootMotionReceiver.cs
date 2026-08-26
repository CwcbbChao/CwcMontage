using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 蒙太奇根运动接收器接口。
    /// 挂载在角色上的移动控制器可实现此接口，直接接收经过掩码过滤后的位移增量与旋转增量。
    /// </summary>
    public interface IMontageRootMotionReceiver
    {
        /// <summary>
        /// 当蒙太奇调度器计算出当帧有效根运动位移增量时调用。
        /// </summary>
        /// <param name="deltaPosition">位移增量向量</param>
        void OnMontageRootMotionDisplacement(Vector3 deltaPosition);

        /// <summary>
        /// 当蒙太奇调度器计算出当帧有效根运动旋转增量时调用。
        /// </summary>
        /// <param name="deltaRotation">旋转增量四元数</param>
        void OnMontageRootMotionRotation(Quaternion deltaRotation);
    }
}
