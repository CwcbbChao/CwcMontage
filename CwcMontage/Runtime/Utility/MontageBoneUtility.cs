using System;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// Core humanoid target bones for action blocks and spatial attachment.
    /// Maps 1:1 to standard Unity Humanoid bones with safe fallback to Root.
    /// </summary>
    public enum MontageTargetBone
    {
        Root = 0,
        Hips = 1,
        Spine = 2,
        Head = 3,
        RightHand = 4,
        LeftHand = 5,
        RightFoot = 6,
        LeftFoot = 7
    }

    /// <summary>
    /// Utility for resolving and caching humanoid bone transforms with zero heap allocations.
    /// </summary>
    public static class MontageBoneUtility
    {
        #region Constants & Static Fields

        public const int BONE_COUNT = 8;

        #endregion

        #region Public Methods

        /// <summary>
        /// Resolves and fills all 8 core bones on the host object in a single pass (zero GC allocation).
        /// </summary>
        /// <param name="targetObject">Host root GameObject</param>
        /// <param name="animator">Target Animator component</param>
        /// <param name="outBones">Destination array for bone Transforms (length must be >= 8)</param>
        public static void ResolveBones(GameObject targetObject, Animator animator, Transform[] outBones)
        {
            if (outBones == null || outBones.Length < BONE_COUNT) return;

            Transform root = targetObject != null ? targetObject.transform : null;
            outBones[(int)MontageTargetBone.Root] = root;

            if (animator != null && animator.isHuman)
            {
                outBones[(int)MontageTargetBone.Hips] = animator.GetBoneTransform(HumanBodyBones.Hips) ?? root;
                outBones[(int)MontageTargetBone.Spine] = animator.GetBoneTransform(HumanBodyBones.Spine) ?? root;
                outBones[(int)MontageTargetBone.Head] = animator.GetBoneTransform(HumanBodyBones.Head) ?? root;
                outBones[(int)MontageTargetBone.RightHand] = animator.GetBoneTransform(HumanBodyBones.RightHand) ?? root;
                outBones[(int)MontageTargetBone.LeftHand] = animator.GetBoneTransform(HumanBodyBones.LeftHand) ?? root;
                outBones[(int)MontageTargetBone.RightFoot] = animator.GetBoneTransform(HumanBodyBones.RightFoot) ?? root;
                outBones[(int)MontageTargetBone.LeftFoot] = animator.GetBoneTransform(HumanBodyBones.LeftFoot) ?? root;
            }
            else
            {
                for (int i = 1; i < BONE_COUNT; i++)
                {
                    outBones[i] = root;
                }
            }
        }

        /// <summary>
        /// Safely resolves a single core bone (with safe root fallback protection).
        /// </summary>
        /// <param name="targetObject">Host root GameObject</param>
        /// <param name="animator">Target Animator component</param>
        /// <param name="boneType">Target core bone enum</param>
        /// <returns>The found bone Transform, or a safe fallback to targetObject.transform if not found</returns>
        public static Transform ResolveBone(GameObject targetObject, Animator animator, MontageTargetBone boneType)
        {
            if (targetObject == null) return null;
            Transform root = targetObject.transform;
            if (boneType == MontageTargetBone.Root || animator == null || !animator.isHuman)
            {
                return root;
            }

            HumanBodyBones humanBone = ToHumanBodyBone(boneType);
            if (humanBone != HumanBodyBones.LastBone)
            {
                var t = animator.GetBoneTransform(humanBone);
                if (t != null) return t;
            }

            return root;
        }

        /// <summary>
        /// Maps the 8 core bone enum to Unity's native HumanBodyBones enum.
        /// </summary>
        public static HumanBodyBones ToHumanBodyBone(MontageTargetBone boneType)
        {
            return boneType switch
            {
                MontageTargetBone.Hips => HumanBodyBones.Hips,
                MontageTargetBone.Spine => HumanBodyBones.Spine,
                MontageTargetBone.Head => HumanBodyBones.Head,
                MontageTargetBone.RightHand => HumanBodyBones.RightHand,
                MontageTargetBone.LeftHand => HumanBodyBones.LeftHand,
                MontageTargetBone.RightFoot => HumanBodyBones.RightFoot,
                MontageTargetBone.LeftFoot => HumanBodyBones.LeftFoot,
                _ => HumanBodyBones.LastBone
            };
        }

        #endregion
    }
}
