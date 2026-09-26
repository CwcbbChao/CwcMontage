using System;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 特效结束或退出时的停止行为。
    /// </summary>
    public enum MontageVFXStopBehavior
    {
        /// <summary>
        /// 停止发射新粒子，允许已生成的残余粒子自然消散完毕后回收。
        /// </summary>
        StopEmitting,

        /// <summary>
        /// 立即清除所有粒子并瞬间归还对象池。
        /// </summary>
        ClearImmediately
    }

    /// <summary>
    /// 视觉特效表现动作块（VFXActionBlock）。
    /// 继承自 MontageSpatialActionBlockBase<VFXState>，运行时 0-GC 且资产完全只读化。
    /// 支持骨骼挂载、世界坐标固定、播放中动态追踪、模拟速率同步与对象池全自动管理，
    /// 全面支持编辑器 3D 视口非运行模式下的实时粒子仿真演化预览。
    /// </summary>
    [Serializable]
    [MontageCategory("Visual")]
    [MontageDisplayName("Play VFX")]
    [MontageColor("#FF8C00")]
    public class VFXActionBlock : MontageSpatialActionBlockBase<VFXActionBlock.VFXState>
    {
        #region 内部状态定义 (纯运行时持有，随槽位与对象池终身复用)

        /// <summary>
        /// 特效动作块运行时动态状态。
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

        #region Inspector 字段

        [Header("VFX Prefab")]
        [Tooltip("包含 ParticleSystem 的特效预制件。")]
        [SerializeField] private GameObject _vfxPrefab;

        [Header("Playback & Stop")]
        [Tooltip("退出动作块时间区间时的停止策略。")]
        [SerializeField] private MontageVFXStopBehavior _stopBehavior = MontageVFXStopBehavior.StopEmitting;

        [Tooltip("是否将粒子系统的模拟速度（simulationSpeed）与蒙太奇播放速率保持同步。")]
        [SerializeField] private bool _playbackRateSynced = true;

        [Header("Clip Trimming & Stretching")]
        [Tooltip("特效截取起始时间（秒）。")]
        [SerializeField] private float _clipStartTime = 0.0f;

        [Tooltip("特效截取结束时间（秒）。以此截取区间为基准定义 1.0x 原速时长，Block 长度提供等比缩放。")]
        [SerializeField] private float _clipEndTime = 0.5f;

        #endregion

        #region 私有非序列化字段

        [NonSerialized] private float _cachedNaturalDuration = -1f;

        #endregion

        #region 公共属性

        public GameObject VFXPrefab => _vfxPrefab;
        public MontageVFXStopBehavior StopBehavior => _stopBehavior;
        public bool PlaybackRateSynced => _playbackRateSynced;

        /// <summary>
        /// 兼容属性：仅在编辑器视口预览时返回当前预览生成的实例引用。运行时请通过 VFXState 访问。
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

        #region 运行时生命周期 (强类型 0-GC 调度)

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

            // 1. 基类解析挂点与计算空间变换（写进 state，零 GC）
            ResolveTargetBone(context, state);
            CalculateWorldTransform(state, out Vector3 worldPos, out Quaternion worldRot);
            Transform attachParent = GetSpawnParent(context, isPreview: false);

            // 2. 从运行时对象池取出实例
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

            // 3. 初始化并驱动 ParticleSystem
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

            // 1. 动态空间追踪与更新
            UpdateSpatialTransform(state.SpawnedInstance, in context, state);

            if (state.CachedParticleSystems == null || state.CachedParticleSystems.Length == 0)
            {
                return;
            }

            // 2. 同步自适应缩放与播放速率变化
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

        #region 编辑器视口预览生命周期 (由 MontageEditorUI 调度)

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

            // 1. 解析挂点并计算空间变换
            ResolveTargetBone(context, state);
            CalculateWorldTransform(state, out Vector3 worldPos, out Quaternion worldRot);
            Transform attachParent = GetSpawnParent(context, isPreview: true);
            if (attachParent == null && context.TargetObject != null)
            {
                attachParent = context.TargetObject.transform;
            }

            // 2. 编辑器视口临时实例化（隔离场景）
            state.SpawnedInstance = UnityEngine.Object.Instantiate(_vfxPrefab, worldPos, worldRot, attachParent);
            if (state.SpawnedInstance == null) return;

            state.SpawnedInstance.name = _vfxPrefab.name;
            state.SpawnedInstance.hideFlags = HideFlags.HideAndDontSave;
            state.SpawnedInstance.transform.localScale = Vector3.Scale(_vfxPrefab.transform.localScale, Scale);

            // 3. 初始化粒子
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

        #region 公共方法与辅助计算

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
