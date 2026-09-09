using System;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 蒙太奇核心目标骨骼枚举。
    /// 收敛为动作游戏中最常用的 8 个人形核心大骨骼，100% 对应 Unity Humanoid 必需骨骼，确保任何模型都能稳健绑定。
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
    /// 蒙太奇骨骼快速解析工具。
    /// 提供 8 核心骨骼的快速映射与安全检索，零静态内存驻留，零堆内存分配。
    /// </summary>
    public static class MontageBoneUtility
    {
        #region 常量与静态字段

        public const int BONE_COUNT = 8;

        #endregion

        #region 公共方法

        /// <summary>
        /// 一次性解析并填充宿主对象上的 8 核心大骨骼数组（零 GC 分配）。
        /// </summary>
        /// <param name="targetObject">宿主根 GameObject</param>
        /// <param name="animator">目标 Animator 组件</param>
        /// <param name="outBones">接收骨骼 Transform 的目标数组（长度必须 >= 8）</param>
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
        /// 单次安全解析特定核心骨骼（带安全回退根节点保护）。
        /// </summary>
        /// <param name="targetObject">宿主根 GameObject</param>
        /// <param name="animator">目标 Animator 组件</param>
        /// <param name="boneType">目标核心骨骼枚举</param>
        /// <returns>找到的骨骼 Transform，若未找到则安全回退返回 targetObject.transform</returns>
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
        /// 将 8 核心骨骼枚举映射为 Unity 原生 HumanBodyBones 枚举。
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
