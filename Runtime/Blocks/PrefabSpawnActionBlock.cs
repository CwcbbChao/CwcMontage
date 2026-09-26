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
    /// 继承自 MontageSpatialActionBlockBase<PrefabSpawnState>，具备骨骼挂载、世界坐标固定、播放中动态追踪位置、
    /// 生命周期托管与对象池回收能力，资产 100% 只读化与运行时 0-GC。
    /// </summary>
    [Serializable]
    [MontageCategory("Gameplay")]
    [MontageDisplayName("Spawn Prefab")]
    [MontageColor("#32CD32")]
    public class PrefabSpawnActionBlock : MontageSpatialActionBlockBase<PrefabSpawnActionBlock.PrefabSpawnState>
    {
        #region 内部状态定义 (纯运行时持有，随槽位复用)

        /// <summary>
        /// 预制件生成动作块运行时动态状态。
        /// </summary>
        public class PrefabSpawnState : MontageSpatialBlockState
        {
            public GameObject SpawnedInstance;
            public float ElapsedTime;

            public override void Reset()
            {
                base.Reset();
                SpawnedInstance = null;
                ElapsedTime = 0f;
            }
        }

        #endregion

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

        #region 公共属性

        public GameObject Prefab => _prefab;
        public MontageSpawnLifecycle Lifecycle => _lifecycle;
        public float CustomDuration => _customDuration;

        /// <summary>
        /// 兼容属性：仅在编辑器视口预览时返回当前预览生成的实例引用。运行时请通过 PrefabSpawnState 访问。
        /// </summary>
        public GameObject SpawnedInstance => GetEditorPreviewState()?.SpawnedInstance;

        #endregion

        #region 运行时生命周期 (强类型 0-GC 调度)

        public override bool CanEnter(in MontageActionContext context)
        {
            return base.CanEnter(context) && _prefab != null;
        }

        protected override void OnEnter(in MontageActionContext context, PrefabSpawnState state)
        {
            if (_prefab == null)
            {
                return;
            }

            state.ElapsedTime = 0f;

            // 1. 基类解析挂点与计算空间变换（写进 state，零 GC）
            ResolveTargetBone(context, state);
            CalculateWorldTransform(state, out Vector3 worldPos, out Quaternion worldRot);
            Transform attachParent = GetSpawnParent(context, isPreview: false);

            // 2. 从运行时对象池生成
            state.SpawnedInstance = MontageObjectPool.Spawn(
                _prefab,
                worldPos,
                worldRot,
                attachParent,
                1.0f);

            if (state.SpawnedInstance == null)
            {
                return;
            }

            state.SpawnedInstance.transform.localScale = Vector3.Scale(_prefab.transform.localScale, Scale);
        }

        protected override void OnUpdate(in MontageActionContext context, PrefabSpawnState state, float deltaTime)
        {
            if (state.SpawnedInstance == null)
            {
                return;
            }

            // 1. 基类统一处理播放中位置/旋转的动态追踪与更新
            UpdateSpatialTransform(state.SpawnedInstance, in context, state);

            // 2. 自定义时长倒计时回收
            if (_lifecycle == MontageSpawnLifecycle.CustomDuration)
            {
                state.ElapsedTime += deltaTime;
                if (state.ElapsedTime >= _customDuration)
                {
                    MontageObjectPool.Recycle(state.SpawnedInstance);
                    state.SpawnedInstance = null;
                }
            }
        }

        protected override void OnExit(in MontageActionContext context, PrefabSpawnState state)
        {
            if (state.SpawnedInstance != null)
            {
                if (_lifecycle == MontageSpawnLifecycle.RecycleOnBlockExit)
                {
                    MontageObjectPool.Recycle(state.SpawnedInstance);
                    state.SpawnedInstance = null;
                }
            }
        }

        #endregion

        #region 编辑器视口预览生命周期 (由 MontageEditorUI 调度)

        public override bool CanPreviewEnter(in MontageActionContext context)
        {
            return base.CanPreviewEnter(context) && _prefab != null;
        }

        public override void OnPreviewEnter(in MontageActionContext context)
        {
            base.OnPreviewEnter(context);

            if (_prefab == null) return;

            var state = GetEditorPreviewState();
            if (state == null) return;

            state.ElapsedTime = 0f;

            ResolveTargetBone(context, state);
            CalculateWorldTransform(state, out Vector3 worldPos, out Quaternion worldRot);
            Transform attachParent = GetSpawnParent(context, isPreview: true);
            if (attachParent == null && context.TargetObject != null)
            {
                attachParent = context.TargetObject.transform;
            }

            state.SpawnedInstance = UnityEngine.Object.Instantiate(
                _prefab,
                worldPos,
                worldRot,
                attachParent);

            if (state.SpawnedInstance == null) return;

            state.SpawnedInstance.name = _prefab.name;
            state.SpawnedInstance.hideFlags = HideFlags.HideAndDontSave;
            state.SpawnedInstance.transform.localScale = Vector3.Scale(_prefab.transform.localScale, Scale);
        }

        public override void OnPreviewUpdate(in MontageActionContext context, float deltaTime)
        {
            var state = GetEditorPreviewState();
            if (state == null || state.SpawnedInstance == null) return;

            UpdateSpatialTransform(state.SpawnedInstance, in context, state);

            if (_lifecycle == MontageSpawnLifecycle.CustomDuration)
            {
                state.ElapsedTime += deltaTime;
                if (state.ElapsedTime >= _customDuration)
                {
                    UnityEngine.Object.DestroyImmediate(state.SpawnedInstance);
                    state.SpawnedInstance = null;
                }
            }
        }

        public override void OnPreviewExit(in MontageActionContext context)
        {
            var state = GetEditorPreviewState();
            if (state != null && state.SpawnedInstance != null)
            {
                UnityEngine.Object.DestroyImmediate(state.SpawnedInstance);
                state.SpawnedInstance = null;
            }

            base.OnPreviewExit(context);
        }

        public override void OnPreviewScrub(in MontageActionContext context, float localTime)
        {
            var state = GetEditorPreviewState();
            if (state == null || state.SpawnedInstance == null) return;

            UpdateSpatialTransform(state.SpawnedInstance, in context, state);
            state.ElapsedTime = localTime;
            if (_lifecycle == MontageSpawnLifecycle.CustomDuration)
            {
                state.SpawnedInstance.SetActive(state.ElapsedTime < _customDuration);
            }
        }

        public override void OnPreviewParametersChanged(in MontageActionContext context)
        {
            var state = GetEditorPreviewState();
            if (state == null || state.SpawnedInstance == null) return;

            UpdatePreviewTransform(state.SpawnedInstance, in context);

            if (_prefab != null)
            {
                state.SpawnedInstance.transform.localScale = Vector3.Scale(_prefab.transform.localScale, Scale);
            }
        }

        public override bool RequiresPreviewRecreate(MontageActionBlockBase newBlock)
        {
            if (newBlock is PrefabSpawnActionBlock newSp)
            {
                return _prefab != newSp._prefab;
            }
            return true;
        }

        #endregion
    }
}
