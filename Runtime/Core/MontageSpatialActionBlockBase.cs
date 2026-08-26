using System;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 蒙太奇空间附着与位置更新模式。
    /// </summary>
    public enum MontageAttachMode
    {
        /// <summary>
        /// 触发瞬间在世界空间生成（快照），之后固定在原地，不随角色或骨骼移动（默认模式）。
        /// 适用于地面扬尘、剑气斩击残留、落雷法阵、受击火花等。
        /// </summary>
        WorldPositionAtStart,

        /// <summary>
        /// 附着为目标骨骼/物体的子对象，直接由 Unity 引擎层父子级驱动位置与旋转实时跟随。
        /// 适用于武器握持道具、手臂附魔光效等。
        /// </summary>
        FollowTarget,

        /// <summary>
        /// 保持在世界空间（不成为子物体），但每帧在 OnUpdate 中实时同步追踪骨骼当前的最新世界坐标与旋转。
        /// 适用于需要保持世界缩放、不受角色骨骼倾斜扭曲但依然跟随角色移动的特效（如脚下光环、头顶标记）。
        /// </summary>
        FollowWorldPositionEveryFrame,

        /// <summary>
        /// 保持在世界空间，每帧仅同步追踪骨骼的世界位置，旋转保持世界固定（不随角色转身旋转）。
        /// 适用于地面范围指示圈、投影法阵等。
        /// </summary>
        FollowPositionOnly
    }

    /// <summary>
    /// 具有空间位置、骨骼挂载与变换更新能力的动作块抽象基类（MontageSpatialActionBlockBase）。
    /// 为 VFXActionBlock、PrefabSpawnActionBlock 等视效与实体生成块提供统一的骨骼检索、
    /// 挂载附着、局部偏移计算与运行时/预览时动态位置同步支持。
    /// </summary>
    [Serializable]
    public abstract class MontageSpatialActionBlockBase : MontageActionBlockBase
    {
        #region Inspector 字段

        [Header("Attachment & Transform")]
        [Tooltip("挂载骨骼或节点名称（如 Weapon_Socket, Hand_R 等）。留空默认以宿主根物体为基准。")]
        [SerializeField] private string _attachBoneName;

        [Tooltip("Humanoid 人型骨骼快捷枚举（若为 LastBone 则根据 _attachBoneName 查找）。")]
        [SerializeField] private HumanBodyBones _humanoidBone = HumanBodyBones.LastBone;

        [Tooltip("附着与位置更新模式：默认为在世界空间生成快照。")]
        [SerializeField] private MontageAttachMode _attachMode = MontageAttachMode.WorldPositionAtStart;

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

        public string AttachBoneName => _attachBoneName;
        public HumanBodyBones HumanoidBone => _humanoidBone;
        public MontageAttachMode AttachMode => _attachMode;
        public Vector3 PositionOffset => _positionOffset;
        public Vector3 RotationOffset => _rotationOffset;
        public Vector3 Scale => _scale;

        #endregion

        #region 空间变换与生命周期辅助方法

        /// <summary>
        /// 在 OnEnter 时解析并缓存目标挂点骨骼 Transform。
        /// </summary>
        protected virtual void ResolveTargetBone(in MontageActionContext context)
        {
            _cachedTargetBone = MontageBoneUtility.FindBone(
                context.TargetObject,
                _attachBoneName,
                _humanoidBone,
                context.Animator);
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
        /// 根据当前的 AttachMode 计算在 Spawn 时应该设置的目标父级 Transform。
        /// </summary>
        protected Transform GetSpawnParent(in MontageActionContext context)
        {
            if (_attachMode == MontageAttachMode.FollowTarget)
            {
                return _cachedTargetBone != null ? _cachedTargetBone : context.TargetObject.transform;
            }

            // 非 FollowTarget 模式：在编辑器预览时挂在宿主下隔离，在运行时保持 null（挂在池根节点下）
            return context.IsPreview ? context.TargetObject.transform : null;
        }

        /// <summary>
        /// 在 OnUpdate 中调用，根据 AttachMode 决定是否在播放过程中动态更新实例在世界空间的位置与旋转。
        /// </summary>
        /// <param name="instance">生成的 GameObject 实例</param>
        /// <param name="context">当前动画上下文</param>
        protected void UpdateSpatialTransform(GameObject instance, in MontageActionContext context)
        {
            if (instance == null)
            {
                return;
            }

            // FollowTarget 模式由 Unity 父子层级自动驱动，无需手动更新
            if (_attachMode == MontageAttachMode.FollowTarget || _attachMode == MontageAttachMode.WorldPositionAtStart)
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

            if (_attachMode == MontageAttachMode.FollowWorldPositionEveryFrame)
            {
                Vector3 currentWorldPos = _cachedTargetBone.TransformPoint(_positionOffset);
                Quaternion currentWorldRot = _cachedTargetBone.rotation * Quaternion.Euler(_rotationOffset);
                instance.transform.SetPositionAndRotation(currentWorldPos, currentWorldRot);
            }
            else if (_attachMode == MontageAttachMode.FollowPositionOnly)
            {
                Vector3 currentWorldPos = _cachedTargetBone.TransformPoint(_positionOffset);
                instance.transform.position = currentWorldPos;
            }
        }

        public override void OnExit(in MontageActionContext context)
        {
            base.OnExit(context);
            _cachedTargetBone = null;
        }

        #endregion
    }
}
