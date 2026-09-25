using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 蒙太奇人形遮罩通用工具类。
    /// 为通用 Humanoid 骨骼提供开箱即用的标准上半身 AvatarMask，实现运行时零配置。
    /// </summary>
    public static class MontageMaskUtility
    {
        #region 静态字段

        private static AvatarMask _cachedHumanoidUpperBodyMask;

        #endregion

        #region 公共方法

        /// <summary>
        /// 获取或创建通用 Humanoid 标准上半身遮罩。
        /// 严格启用人体身体、头部与双臂双掌，禁用根节点与双腿双脚。
        /// </summary>
        /// <returns>全局缓存的标准 Humanoid 上半身 AvatarMask 实例</returns>
        public static AvatarMask GetOrCreateHumanoidUpperBodyMask()
        {
            if (_cachedHumanoidUpperBodyMask != null)
            {
                return _cachedHumanoidUpperBodyMask;
            }

            var mask = new AvatarMask();
            mask.name = "Humanoid_UpperBody_Mask";

            // 禁用根节点与下肢骨骼及脚部 IK
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Root, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK, false);

            // 启用躯干、头部、双臂、手指与手部 IK
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftHandIK, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightHandIK, true);

            _cachedHumanoidUpperBodyMask = mask;
            return _cachedHumanoidUpperBodyMask;
        }

        #endregion
    }
}
