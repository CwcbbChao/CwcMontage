using System;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 预制件生命周期策略。
    /// </summary>
    public enum MontageSpawnLifecycle
    {
        /// <summary>
        /// 在动作块时间区间结束或被打断时自动回收（适合武器刀光网格、附魔残影、招式辅助模型等）。
        /// </summary>
        RecycleOnBlockExit,

        /// <summary>
        /// 按指定的固定秒数倒计时回收。
        /// </summary>
        CustomDuration,

        /// <summary>
        /// 独立生命周期（生成后由 Prefab 自身脚本或外部系统管理，蒙太奇不主动回收）。
        /// </summary>
        Independent
    }

    /// <summary>
    /// 预制件生成与挂载动作块（PrefabSpawnActionBlock）。
    /// 继承自 MontageSpatialActionBlockBase，具备完整的骨骼挂载、世界坐标固定、播放中动态追踪位置、
    /// 生命周期托管与对象池回收能力，全面支持编辑器 3D 视口预览并确保离开时绝对无残留。
    /// </summary>
    [Serializable]
    [MontageCategory("Gameplay")]
    [MontageDisplayName("Spawn Prefab")]
    [MontageColor("#32CD32")]
    public class PrefabSpawnActionBlock : MontageSpatialActionBlockBase
    {
        #region Inspector 字段

        [Header("Prefab Reference")]
        [Tooltip("要生成的 GameObject 预制件。")]
        [SerializeField] private GameObject _prefab;

        [Header("Lifecycle Management")]
        [Tooltip("预制件的生命周期回收策略。")]
        [SerializeField] private MontageSpawnLifecycle _lifecycle = MontageSpawnLifecycle.RecycleOnBlockExit;

        [Min(0.01f)]
        [Tooltip("当生命周期为 CustomDuration 时的存活持续时间（秒）。")]
        [SerializeField] private float _customDuration = 2.0f;

        #endregion

        #region 私有非序列化运行时字段

        [NonSerialized] private GameObject _spawnedInstance;
        [NonSerialized] private float _elapsedTime;

        #endregion

        #region 公共属性

        public GameObject Prefab => _prefab;
        public MontageSpawnLifecycle Lifecycle => _lifecycle;
        public float CustomDuration => _customDuration;

        #endregion

        #region 动作块生命周期

        public override bool CanEnter(in MontageActionContext context)
        {
            return base.CanEnter(context) && _prefab != null;
        }

        public override void OnEnter(in MontageActionContext context)
        {
            base.OnEnter(context);

            if (_prefab == null)
            {
                return;
            }

            _elapsedTime = 0f;

            // 1. 基类解析挂点与计算空间变换
            ResolveTargetBone(context);
            CalculateWorldTransform(out Vector3 worldPos, out Quaternion worldRot);
            Transform attachParent = GetSpawnParent(context);

            // 2. 从对象池生成
            _spawnedInstance = MontageObjectPool.Spawn(
                _prefab,
                worldPos,
                worldRot,
                attachParent,
                1.0f,
                context.IsPreview);

            if (_spawnedInstance == null)
            {
                return;
            }

            _spawnedInstance.transform.localScale = Vector3.Scale(_prefab.transform.localScale, Scale);
        }

        public override void OnUpdate(in MontageActionContext context, float deltaTime)
        {
            if (_spawnedInstance == null)
            {
                return;
            }

            // 1. 基类统一处理播放中位置/旋转的动态追踪与更新
            UpdateSpatialTransform(_spawnedInstance, in context);

            // 2. 自定义时长倒计时回收
            if (_lifecycle == MontageSpawnLifecycle.CustomDuration)
            {
                _elapsedTime += deltaTime;
                if (_elapsedTime >= _customDuration)
                {
                    MontageObjectPool.Recycle(_spawnedInstance, context.IsPreview);
                    _spawnedInstance = null;
                }
            }
        }

        public override void OnExit(in MontageActionContext context)
        {
            if (_spawnedInstance != null)
            {
                // 在编辑器预览模式下，无论任何 Lifecycle 策略均强制安全回收，杜绝视口残留孤儿对象
                if (context.IsPreview || _lifecycle == MontageSpawnLifecycle.RecycleOnBlockExit)
                {
                    MontageObjectPool.Recycle(_spawnedInstance, context.IsPreview);
                    _spawnedInstance = null;
                }
            }

            base.OnExit(context);
        }

        #endregion
    }
}
