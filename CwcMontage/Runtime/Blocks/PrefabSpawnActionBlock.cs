using System;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// Lifecycle and recycling policies for spawned prefabs.
    /// </summary>
    public enum MontageSpawnLifecycle
    {
        /// <summary>
        /// Automatically recycles when exiting the block time range or on interrupt.
        /// </summary>
        RecycleOnBlockExit,

        /// <summary>
        /// Recycles after a fixed duration in seconds.
        /// </summary>
        CustomDuration,

        /// <summary>
        /// Independent lifecycle managed by external scripts or the prefab itself.
        /// </summary>
        Independent
    }

    /// <summary>
    /// Action block for instantiating and pooling prefabs (weapons, weapon trails, props, hitboxes).
    /// Supports bone attachment, world locking, lifecycle policies, and zero-allocation pooling.
    /// </summary>
    [Serializable]
    [MontageCategory("Gameplay")]
    [MontageDisplayName("Spawn Prefab")]
    [MontageColor("#32CD32")]
    public class PrefabSpawnActionBlock : MontageSpatialActionBlockBase<PrefabSpawnActionBlock.PrefabSpawnState>
    {
        #region Internal State Definition

        /// <summary>
        /// Runtime dynamic state for PrefabSpawnActionBlock.
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

        #region Serialized Fields

        [Header("Prefab Reference")]
        [Tooltip("Prefab GameObject to instantiate.")]
        [SerializeField] private GameObject _prefab;

        [Header("Lifecycle Management")]
        [Tooltip("Lifecycle recycling policy for the spawned instance.")]
        [SerializeField] private MontageSpawnLifecycle _lifecycle = MontageSpawnLifecycle.RecycleOnBlockExit;

        [Min(0.01f)]
        [Tooltip("Lifetime duration in seconds when Lifecycle is set to CustomDuration.")]
        [SerializeField] private float _customDuration = 2.0f;

        #endregion

        #region Properties

        public GameObject Prefab => _prefab;
        public MontageSpawnLifecycle Lifecycle => _lifecycle;
        public float CustomDuration => _customDuration;

        /// <summary>
        /// Compatibility property: returns the preview instance reference only in editor viewport preview. At runtime, access it through PrefabSpawnState.
        /// </summary>
        public GameObject SpawnedInstance => GetEditorPreviewState()?.SpawnedInstance;

        #endregion

        #region Runtime Lifecycle (0-GC Typed Dispatch)

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

            // 1. Base class resolves the attachment bone and calculates the spatial transform (written into state, zero GC)
            ResolveTargetBone(context, state);
            CalculateWorldTransform(state, out Vector3 worldPos, out Quaternion worldRot);
            Transform attachParent = GetSpawnParent(context, isPreview: false);

            // 2. Spawn from the runtime object pool
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

            // 1. Base class uniformly handles dynamic tracking and updating of position/rotation during playback
            UpdateSpatialTransform(state.SpawnedInstance, in context, state);

            // 2. Recycle after the custom-duration countdown
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

        #region Editor Preview Lifecycle

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
