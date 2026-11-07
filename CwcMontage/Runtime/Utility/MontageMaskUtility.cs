using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// General-purpose utility for montage humanoid masks.
    /// Provides an out-of-the-box standard upper-body AvatarMask for generic Humanoid rigs, requiring zero runtime configuration.
    /// </summary>
    public static class MontageMaskUtility
    {
        #region Static Fields

        private static AvatarMask _cachedHumanoidUpperBodyMask;

        #endregion

        #region Public Methods

        /// <summary>
        /// Gets or creates the standard generic Humanoid upper-body mask.
        /// Strictly enables the body, head, both arms and hands, and disables the root, both legs and feet.
        /// </summary>
        /// <returns>Globally cached standard Humanoid upper-body AvatarMask instance</returns>
        public static AvatarMask GetOrCreateHumanoidUpperBodyMask()
        {
            if (_cachedHumanoidUpperBodyMask != null)
            {
                return _cachedHumanoidUpperBodyMask;
            }

            var mask = new AvatarMask();
            mask.name = "Humanoid_UpperBody_Mask";

            // Disable the root, lower-body bones, and foot IK
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Root, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK, false);

            // Enable the torso, head, arms, fingers, and hand IK
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
