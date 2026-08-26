using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 蒙太奇骨骼与挂载点检索工具。
    /// 提供 Humanoid 骨骼映射、名称精准/模糊匹配以及高速缓存机制，
    /// 避免在 ActionBlock 触发时频繁进行深层 Hierarchy 遍历。
    /// </summary>
    public static class MontageBoneUtility
    {
        #region 私有静态字段

        private static readonly Dictionary<int, Dictionary<string, Transform>> _boneCache = new(32);

        #endregion

        #region 公共方法

        /// <summary>
        /// 查找目标宿主对象上的指定骨骼 Transform。
        /// </summary>
        /// <param name="targetObject">宿主根 GameObject</param>
        /// <param name="boneName">骨骼或挂点名称（可选）</param>
        /// <param name="humanoidBone">HumanBodyBones 枚举（可选）</param>
        /// <param name="targetAnimator">目标 Animator 组件（可选）</param>
        /// <returns>找到的骨骼 Transform，若未找到则安全回退返回 targetObject.transform</returns>
        public static Transform FindBone(
            GameObject targetObject,
            string boneName = null,
            HumanBodyBones humanoidBone = HumanBodyBones.LastBone,
            Animator targetAnimator = null)
        {
            if (targetObject == null)
            {
                return null;
            }

            // 1. 若指定了具体的 Humanoid 骨骼（非 LastBone）且存在有效 Animator，优先通过 Animator 获取
            if (humanoidBone != HumanBodyBones.LastBone)
            {
                var anim = targetAnimator != null ? targetAnimator : targetObject.GetComponentInChildren<Animator>();
                if (anim != null && anim.isHuman)
                {
                    var boneTransform = anim.GetBoneTransform(humanoidBone);
                    if (boneTransform != null)
                    {
                        return boneTransform;
                    }
                }
            }

            // 2. 若未配置骨骼名称，直接返回根物体
            if (string.IsNullOrWhiteSpace(boneName))
            {
                return targetObject.transform;
            }

            int rootId = targetObject.GetInstanceID();
            if (!_boneCache.TryGetValue(rootId, out var dict))
            {
                dict = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
                _boneCache[rootId] = dict;
            }

            if (dict.TryGetValue(boneName, out var cachedTransform))
            {
                if (cachedTransform != null)
                {
                    return cachedTransform;
                }
                dict.Remove(boneName);
            }

            // 3. 在所有子节点中按名称查找
            var allTransforms = targetObject.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < allTransforms.Length; i++)
            {
                var t = allTransforms[i];
                if (string.Equals(t.name, boneName, StringComparison.OrdinalIgnoreCase))
                {
                    dict[boneName] = t;
                    return t;
                }
            }

            // 4. 尝试包含匹配
            for (int i = 0; i < allTransforms.Length; i++)
            {
                var t = allTransforms[i];
                if (t.name.IndexOf(boneName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    dict[boneName] = t;
                    return t;
                }
            }

            // 5. 未匹配到具体骨骼时安全回退
            dict[boneName] = targetObject.transform;
            return targetObject.transform;
        }

        /// <summary>
        /// 清理指定宿主对象的骨骼缓存（在角色销毁时调用）。
        /// </summary>
        public static void ClearCache(GameObject targetObject)
        {
            if (targetObject == null) return;
            _boneCache.Remove(targetObject.GetInstanceID());
        }

        /// <summary>
        /// 清理全局所有骨骼缓存。
        /// </summary>
        public static void ClearAllCache()
        {
            _boneCache.Clear();
        }

        #endregion
    }
}
