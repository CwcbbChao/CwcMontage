using System;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// Stop behavior for particle systems when leaving the action block.
    /// </summary>
    public enum MontageVFXStopBehavior
    {
        /// <summary>
        /// Stops emitting new particles and allows existing particles to fade out naturally.
        /// </summary>
        StopEmitting,

        /// <summary>
        /// Clears all particles immediately and returns to object pool.
        /// </summary>
        ClearImmediately
    }

    /// <summary>
    /// Action block for spawning, tracking, and simulating visual particle effects (VFX).
    /// Supports bone attachment, world locking, simulation speed sync, and zero-allocation pooling.
    /// </summary>
    [Serializable]
    [MontageCategory("Visual")]
    [MontageDisplayName("Play VFX")]
    [MontageColor("#FF8C00")]
    public class VFXActionBlock : MontageSpatialActionBlockBase<VFXActionBlock.VFXState>
    {
        #region Internal State Definition

        /// <summary>
        /// Runtime dynamic state for VFXActionBlock.
        /// </summary>
        public class VFXState : MontageSpatialBlockState
        {
            public GameObject SpawnedInstance;
            public ParticleSystem[] CachedParticleSystems;

            public override void Reset()
            {
                base.Reset();
                SpawnedInstance = null;
                CachedParticleSystems = null;
            }
        }

        #endregion

        #region Serialized Fields

        [Header("VFX Prefab")]
        [Tooltip("Prefab containing ParticleSystem components.")]
        [SerializeField] private GameObject _vfxPrefab;

        [Header("Playback & Stop")]
        [Tooltip("Stopping policy when exiting the block time range.")]
        [SerializeField] private MontageVFXStopBehavior _stopBehavior = MontageVFXStopBehavior.StopEmitting;

        [Tooltip("Whether to sync ParticleSystem simulation speed with montage playback rate.")]
        [SerializeField] private bool _playbackRateSynced = true;

        [Header("Clip Trimming & Stretching")]
        [Tooltip("VFX clip start time in seconds.")]
        [SerializeField] private float _clipStartTime = 0.0f;

        [Tooltip("VFX clip end time in seconds.")]
        [SerializeField] private float _clipEndTime = 0.5f;

        #endregion

        #region Private Non-Serialized Fields

        [NonSerialized] private float _cachedNaturalDuration = -1f;

        #endregion

        #region Properties

        public GameObject VFXPrefab => _vfxPrefab;
        public MontageVFXStopBehavior StopBehavior => _stopBehavior;
        public bool PlaybackRateSynced => _playbackRateSynced;

        /// <summary>
        /// Compatibility property: returns the preview-generated instance reference only during editor viewport preview. At runtime, access it through VFXState.
        /// </summary>
        public GameObject SpawnedInstance => GetEditorPreviewState()?.SpawnedInstance;

        public override bool IsTrimmableClip => true;

        public override float ClipStartTime
        {
            get => _clipStartTime;
            set => _clipStartTime = Mathf.Max(0f, value);
        }

        public override float ClipEndTime
        {
            get => _clipEndTime > 0.0001f ? _clipEndTime : Mathf.Max(0.1f, BlockDuration);
            set => _clipEndTime = Mathf.Max(_clipStartTime + 0.001f, value);
        }

        #endregion

        #region Runtime Lifecycle (0-GC Typed Dispatch)

        public override bool CanEnter(in MontageActionContext context)
        {
            return base.CanEnter(context) && _vfxPrefab != null;
        }

        protected override void OnEnter(in MontageActionContext context, VFXState state)
        {
            if (_vfxPrefab == null)
            {
                return;
            }

            // 1. Base class resolves the attach point and computes the spatial transform (writes into state, zero GC).
            ResolveTargetBone(context, state);
            CalculateWorldTransform(state, out Vector3 worldPos, out Quaternion worldRot);
            Transform attachParent = GetSpawnParent(context, isPreview: false);

            // 2. Spawn the instance from the runtime object pool.
            state.SpawnedInstance = MontageObjectPool.Spawn(
                _vfxPrefab,
                worldPos,
                worldRot,
                attachParent,
                1.0f);

            if (state.SpawnedInstance == null)
            {
                return;
            }

            state.SpawnedInstance.transform.localScale = Vector3.Scale(_vfxPrefab.transform.localScale, Scale);

            // 3. Initialize and drive the ParticleSystems.
            state.CachedParticleSystems = state.SpawnedInstance.GetComponentsInChildren<ParticleSystem>(true);
            float speedScale = GetStretchSpeedMultiplier();
            float simRate = speedScale * (_playbackRateSynced ? Mathf.Max(0.001f, context.PlaybackRate) : 1.0f);

            for (int i = 0; i < state.CachedParticleSystems.Length; i++)
            {
                var ps = state.CachedParticleSystems[i];
                if (ps != null)
                {
                    var main = ps.main;
                    main.simulationSpeed = simRate;
                    ps.Play(true);
                }
            }
        }

        protected override void OnUpdate(in MontageActionContext context, VFXState state, float deltaTime)
        {
            if (state.SpawnedInstance == null)
            {
                return;
            }

            // 1. Dynamic spatial tracking and update.
            UpdateSpatialTransform(state.SpawnedInstance, in context, state);

            if (state.CachedParticleSystems == null || state.CachedParticleSystems.Length == 0)
            {
                return;
            }

            // 2. Sync adaptive scaling and playback rate changes.
            float speedScale = GetStretchSpeedMultiplier();
            float simRate = speedScale * (_playbackRateSynced ? Mathf.Max(0.001f, context.PlaybackRate) : 1.0f);
            for (int i = 0; i < state.CachedParticleSystems.Length; i++)
            {
                var ps = state.CachedParticleSystems[i];
                if (ps != null)
                {
                    var main = ps.main;
                    main.simulationSpeed = simRate;
                }
            }
        }

        protected override void OnExit(in MontageActionContext context, VFXState state)
        {
            if (state.SpawnedInstance != null)
            {
                if (state.CachedParticleSystems != null)
                {
                    for (int i = 0; i < state.CachedParticleSystems.Length; i++)
                    {
                        var ps = state.CachedParticleSystems[i];
                        if (ps != null)
                        {
                            if (_stopBehavior == MontageVFXStopBehavior.ClearImmediately)
                            {
                                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                                ps.Clear(true);
                            }
                            else
                            {
                                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                            }
                        }
                    }
                }

                MontageObjectPool.Recycle(state.SpawnedInstance);
                state.SpawnedInstance = null;
            }

            state.CachedParticleSystems = null;
        }

        #endregion

        #region Editor Preview Lifecycle

        public override bool CanPreviewEnter(in MontageActionContext context)
        {
            return base.CanPreviewEnter(context) && _vfxPrefab != null;
        }

        public override void OnPreviewEnter(in MontageActionContext context)
        {
            base.OnPreviewEnter(context);

            if (_vfxPrefab == null) return;

            var state = GetEditorPreviewState();
            if (state == null) return;

            // 1. Resolve the attach point and compute the spatial transform.
            ResolveTargetBone(context, state);
            CalculateWorldTransform(state, out Vector3 worldPos, out Quaternion worldRot);
            Transform attachParent = GetSpawnParent(context, isPreview: true);
            if (attachParent == null && context.TargetObject != null)
            {
                attachParent = context.TargetObject.transform;
            }

            // 2. Temporary editor viewport instantiation (isolated scene).
            state.SpawnedInstance = UnityEngine.Object.Instantiate(_vfxPrefab, worldPos, worldRot, attachParent);
            if (state.SpawnedInstance == null) return;

            state.SpawnedInstance.name = _vfxPrefab.name;
            state.SpawnedInstance.hideFlags = HideFlags.HideAndDontSave;
            state.SpawnedInstance.transform.localScale = Vector3.Scale(_vfxPrefab.transform.localScale, Scale);

            // 3. Initialize the particles.
            state.CachedParticleSystems = state.SpawnedInstance.GetComponentsInChildren<ParticleSystem>(true);
            float speedScale = GetStretchSpeedMultiplier();
            float simRate = speedScale * (_playbackRateSynced ? Mathf.Max(0.001f, context.PlaybackRate) : 1.0f);

            for (int i = 0; i < state.CachedParticleSystems.Length; i++)
            {
                var ps = state.CachedParticleSystems[i];
                if (ps != null)
                {
                    var main = ps.main;
                    main.simulationSpeed = simRate;
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ps.Clear(true);
                    ps.Play(true);
                }
            }
        }

        public override void OnPreviewUpdate(in MontageActionContext context, float deltaTime)
        {
            var state = GetEditorPreviewState();
            if (state == null || state.SpawnedInstance == null) return;

            UpdateSpatialTransform(state.SpawnedInstance, in context, state);

            if (state.CachedParticleSystems == null || state.CachedParticleSystems.Length == 0) return;

            float speedScale = GetStretchSpeedMultiplier();
            float simRate = speedScale * (_playbackRateSynced ? Mathf.Max(0.001f, context.PlaybackRate) : 1.0f);

            for (int i = 0; i < state.CachedParticleSystems.Length; i++)
            {
                var ps = state.CachedParticleSystems[i];
                if (ps != null)
                {
                    var main = ps.main;
                    main.simulationSpeed = simRate;
                }
            }

            float effectiveStep = deltaTime * speedScale * (_playbackRateSynced ? context.PlaybackRate : 1.0f);
            if (effectiveStep > 0.0001f)
            {
                for (int i = 0; i < state.CachedParticleSystems.Length; i++)
                {
                    var ps = state.CachedParticleSystems[i];
                    if (ps != null)
                    {
                        ps.Simulate(effectiveStep, true, false);
                    }
                }
            }
        }

        public override void OnPreviewExit(in MontageActionContext context)
        {
            var state = GetEditorPreviewState();
            if (state != null && state.SpawnedInstance != null)
            {
                if (state.CachedParticleSystems != null)
                {
                    for (int i = 0; i < state.CachedParticleSystems.Length; i++)
                    {
                        var ps = state.CachedParticleSystems[i];
                        if (ps != null)
                        {
                            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                            ps.Clear(true);
                        }
                    }
                }

                UnityEngine.Object.DestroyImmediate(state.SpawnedInstance);
                state.SpawnedInstance = null;
                state.CachedParticleSystems = null;
            }

            base.OnPreviewExit(context);
        }

        public void SimulateToTime(float targetLocalTime)
        {
            var state = GetEditorPreviewState();
            if (state == null || state.CachedParticleSystems == null || state.CachedParticleSystems.Length == 0)
            {
                return;
            }

            targetLocalTime = Mathf.Max(0f, targetLocalTime);
            for (int i = 0; i < state.CachedParticleSystems.Length; i++)
            {
                var ps = state.CachedParticleSystems[i];
                if (ps != null)
                {
                    ps.Simulate(targetLocalTime, true, true, true);
                }
            }
        }

        public override void OnPreviewScrub(in MontageActionContext context, float localTime)
        {
            var state = GetEditorPreviewState();
            if (state == null || state.SpawnedInstance == null) return;

            UpdateSpatialTransform(state.SpawnedInstance, in context, state);

            float blockDur = Mathf.Max(0.0001f, BlockDuration);
            float progress = Mathf.Clamp01(localTime / blockDur);
            float targetTime = ClipStartTime + progress * EffectiveClipDuration;

            SimulateToTime(targetTime);
        }

        public override void OnPreviewParametersChanged(in MontageActionContext context)
        {
            var state = GetEditorPreviewState();
            if (state == null || state.SpawnedInstance == null) return;

            UpdatePreviewTransform(state.SpawnedInstance, in context);

            if (_vfxPrefab != null)
            {
                state.SpawnedInstance.transform.localScale = Vector3.Scale(_vfxPrefab.transform.localScale, Scale);
            }
        }

        public override bool RequiresPreviewRecreate(MontageActionBlockBase newBlock)
        {
            if (newBlock is VFXActionBlock newVfx)
            {
                if (_vfxPrefab != newVfx._vfxPrefab)
                {
                    _cachedNaturalDuration = -1f;
                    return true;
                }
                return false;
            }
            return true;
        }

        #endregion

        #region Public Methods & Calculations

        public float GetNaturalDuration()
        {
            if (_cachedNaturalDuration > 0.01f)
            {
                return _cachedNaturalDuration;
            }

            if (_vfxPrefab == null)
            {
                return Mathf.Max(0.01f, BlockDuration);
            }

            _cachedNaturalDuration = CalculatePrefabTotalDuration(_vfxPrefab);
            return _cachedNaturalDuration;
        }

        public float GetStretchSpeedMultiplier()
        {
            return SpeedMultiplier;
        }

        public static float CalculatePrefabTotalDuration(GameObject prefab)
        {
            if (prefab == null) return 1.0f;
            var systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
            if (systems == null || systems.Length == 0) return 1.0f;

            float maxNonLooping = 0f;
            float maxLooping = 0f;

            for (int i = 0; i < systems.Length; i++)
            {
                var ps = systems[i];
                if (ps == null) continue;
                var main = ps.main;

                float delay = main.startDelay.mode == ParticleSystemCurveMode.TwoConstants
                    ? main.startDelay.constantMax
                    : main.startDelay.constant;

                float lifetime = main.startLifetime.mode == ParticleSystemCurveMode.TwoConstants
                    ? main.startLifetime.constantMax
                    : main.startLifetime.constant;

                float duration = main.duration;

                if (main.loop)
                {
                    maxLooping = Mathf.Max(maxLooping, delay + duration);
                }
                else
                {
                    maxNonLooping = Mathf.Max(maxNonLooping, delay + duration + lifetime);
                }
            }

            float result = maxNonLooping > 0.001f ? maxNonLooping : maxLooping;
            return Mathf.Max(0.01f, result);
        }

        public override string GetTimingCustomHint()
        {
            if (_vfxPrefab == null) return null;
            return $"Clip: {ClipStartTime:F2}s - {ClipEndTime:F2}s ({EffectiveClipDuration:F2}s) | Speed: {SpeedMultiplier:F2}x";
        }

        #endregion
    }
}
