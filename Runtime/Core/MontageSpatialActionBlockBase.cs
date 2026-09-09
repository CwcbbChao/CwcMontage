using System;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 蒙太奇空间附着与位置更新模式。
    /// 所有跟随模式均由代码在外部实时驱动世界变换，绝不挂载为角色骨骼子节点，杜绝角色缩放污染与骨骼层级脏化。
    /// </summary>
    public enum MontageAttachMode
    {
        /// <summary>
        /// 外部驱动实时同步目标骨骼的世界位置与世界旋转（默认模式）。
        /// 始终保持在世界独立层级，完全免疫角色的非等比缩放、形变与挤压。
        /// </summary>
        FollowTarget = 0,

        /// <summary>
        /// 外部驱动仅实时同步目标骨骼的世界位置，旋转保持世界固定。
        /// 适用于地面范围指示圈、投影法阵等不随角色转身打转的表现。
        /// </summary>
        FollowPositionOnly = 1,

        /// <summary>
        /// 仅在触发瞬间快照世界坐标与世界旋转，之后固定在原地，不随角色移动。
        /// 适用于极少数原地停留的表现（如地面落点法阵残留）。
        /// </summary>
        WorldPositionAtStart = 2
    }

    /// <summary>
    /// 具有空间位置、骨骼挂载与变换更新能力的动作块抽象基类（MontageSpatialActionBlockBase）。
    /// 为 VFXActionBlock、PrefabSpawnActionBlock 等视效与实体生成块提供 8 核心骨骼绑定、
    /// 局部偏移计算与外部驱动的零侵入动态位置同步支持。
    /// </summary>
    [Serializable]
    public abstract class MontageSpatialActionBlockBase : MontageActionBlockBase
    {
        #region Inspector 字段

        [Header("Attachment & Transform")]
        [Tooltip("目标挂点骨骼（精简收敛为 8 个人形核心大骨骼，100% 自动适配所有 Humanoid 模型）。")]
        [SerializeField] private MontageTargetBone _targetBone = MontageTargetBone.Root;

        [Tooltip("附着与位置更新模式：默认采用外部驱动实时跟随目标骨骼。")]
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
