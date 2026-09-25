using System;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 空间附着模式。
    /// 通过外部代码同步目标骨骼的世界变换，不挂载为骨骼子节点，避免受角色骨骼非等比缩放影响。
    /// </summary>
    public enum MontageAttachMode
    {
        /// <summary>
        /// 实时同步目标骨骼的世界位置与世界旋转（默认模式）。
        /// </summary>
        FollowTarget = 0,

        /// <summary>
        /// 仅实时同步目标骨骼的世界位置，旋转保持世界固定（适用于地面光圈/法阵等）。
        /// </summary>
        FollowPositionOnly = 1,

        /// <summary>
        /// 仅在触发瞬间记录目标世界坐标与旋转，之后固定在原地。
        /// </summary>
        WorldPositionAtStart = 2
    }

    /// <summary>
    /// 具备空间挂载与位置同步能力的动作块抽象基类。
    /// 为 VFXActionBlock、PrefabSpawnActionBlock 等提供骨骼挂点绑定、局部偏移计算与位置同步支持。
    /// </summary>
    [Serializable]
    public abstract class MontageSpatialActionBlockBase : MontageActionBlockBase
    {
        #region Inspector 字段

        [Header("Attachment & Transform")]
        [Tooltip("目标挂点骨骼（支持 8 个人形常用核心骨骼与 Root）。")]
        [SerializeField] private MontageTargetBone _targetBone = MontageTargetBone.Root;

        [Tooltip("附着与位置同步模式。")]
        [SerializeField] private MontageAttachMode _attachMode = MontageAttachMode.FollowTarget;

        [Tooltip("相对于目标挂点的局部位置偏移。")]
        [SerializeField] private Vector3 _positionOffset = Vector3.zero;

        [Tooltip("相对于目标挂点的局部旋转偏移（欧拉角）。")]
        [SerializeField] private Vector3 _rotationOffset = Vector3.zero;

        [Tooltip("相对于预制件的局部缩放倍率。")]
        [SerializeField] private Vector3 _scale = Vector3.one;

        #endregion

        #region 私有与受保护的非序列化运行时字段

        [NonSerialized] protected Transform _cachedTargetBone;

        #endregion

        #region 公共属性

        public MontageTargetBone TargetBone => _targetBone;
        public MontageAttachMode AttachMode => _attachMode;
        public Vector3 PositionOffset => _positionOffset;
        public Vector3 RotationOffset => _rotationOffset;
        public Vector3 Scale => _scale;

        #endregion

        #region 空间变换与生命周期辅助方法

        /// <summary>
        /// 在 OnEnter 时解析并缓存目标挂点骨骼 Transform（零 GC 分配）。
        /// </summary>
        protected virtual void ResolveTargetBone(in MontageActionContext context)
        {
            _cachedTargetBone = context.GetTargetBone(_targetBone);
        }

        /// <summary>
        /// 根据当前挂点骨骼与配置的偏移量，计算出目标世界坐标与世界旋转。
        /// </summary>
        /// <param name="worldPos">计算出的世界坐标</param>
        /// <param name="worldRot">计算出的世界旋转</param>
        protected void CalculateWorldTransform(out Vector3 worldPos, out Quaternion worldRot)
        {
            if (_cachedTargetBone != null)
            {
                worldPos = _cachedTargetBone.TransformPoint(_positionOffset);
                worldRot = _cachedTargetBone.rotation * Quaternion.Euler(_rotationOffset);
            }
            else
            {
                worldPos = _positionOffset;
                worldRot = Quaternion.Euler(_rotationOffset);
            }
        }

        /// <summary>
        /// 计算在 Spawn 时应该设置的目标父级 Transform。
        /// 严格杜绝将实例挂载为角色骨骼子物体，彻底免疫角色局部缩放畸变。
        /// </summary>
        protected Transform GetSpawnParent(in MontageActionContext context, bool isPreview = false)
        {
            // 预览时挂在视口宿主根节点下隔离，运行时保持为 null（挂在对象池常驻根节点下）
            return isPreview && context.TargetObject != null ? context.TargetObject.transform : null;
        }

        /// <summary>
        /// 在 OnUpdate 或 OnPreviewUpdate 中调用，根据 AttachMode 外部驱动更新实例在世界空间的位置与旋转。
        /// </summary>
        /// <param name="instance">生成的 GameObject 实例</param>
        /// <param name="context">当前动画上下文</param>
        protected void UpdateSpatialTransform(GameObject instance, in MontageActionContext context)
        {
            if (instance == null || _attachMode == MontageAttachMode.WorldPositionAtStart)
            {
                return;
            }

            if (_cachedTargetBone == null)
            {
                ResolveTargetBone(context);
                if (_cachedTargetBone == null)
                {
                    return;
                }
            }

            Vector3 currentWorldPos = _cachedTargetBone.TransformPoint(_positionOffset);

            if (_attachMode == MontageAttachMode.FollowTarget)
            {
                Quaternion currentWorldRot = _cachedTargetBone.rotation * Quaternion.Euler(_rotationOffset);
                instance.transform.SetPositionAndRotation(currentWorldPos, currentWorldRot);
            }
            else if (_attachMode == MontageAttachMode.FollowPositionOnly)
            {
                instance.transform.position = currentWorldPos;
            }
        }

        public override void OnExit(in MontageActionContext context)
        {
            base.OnExit(context);
            _cachedTargetBone = null;
        }

        public override void OnPreviewExit(in MontageActionContext context)
        {
            base.OnPreviewExit(context);
            _cachedTargetBone = null;
        }

        /// <summary>
        /// 当在编辑器视口预览中外部修改了空间变换属性（位置偏移、旋转偏移、缩放或骨骼）时，
        /// 原地即时同步并更新预览物体的 Transform，无需重新销毁并重建。
        /// </summary>
        /// <param name="instance">生成的 GameObject 实例</param>
        /// <param name="context">当前动画上下文</param>
        public virtual void UpdatePreviewTransform(GameObject instance, in MontageActionContext context)
        {
            if (instance == null) return;

            ResolveTargetBone(context);
            CalculateWorldTransform(out Vector3 worldPos, out Quaternion worldRot);

            if (_attachMode == MontageAttachMode.FollowPositionOnly)
            {
                instance.transform.position = worldPos;
            }
            else
            {
                instance.transform.SetPositionAndRotation(worldPos, worldRot);
            }
        }

        #endregion
    }
}
