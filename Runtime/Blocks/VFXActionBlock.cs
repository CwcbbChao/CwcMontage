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
    /// 继承自 MontageSpatialActionBlockBase，具备完整的骨骼挂载、世界坐标固定、播放中每帧动态追踪、
    /// 模拟速率同步与对象池全自动管理能力，全面支持编辑器 3D 视口非运行模式下的实时粒子仿真演化预览。
    /// </summary>
    [Serializable]
    [MontageCategory("Visual")]
    [MontageDisplayName("Play VFX")]
    [MontageColor("#FF8C00")]
    public class VFXActionBlock : MontageSpatialActionBlockBase
    {
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

        #region 私有非序列化运行时字段

        [NonSerialized] private GameObject _spawnedInstance;
        [NonSerialized] private ParticleSystem[] _cachedParticleSystems;
        [NonSerialized] private float _cachedNaturalDuration = -1f;

        #endregion

        #region 公共属性

        public GameObject VFXPrefab => _vfxPrefab;
        public MontageVFXStopBehavior StopBehavior => _stopBehavior;
        public bool PlaybackRateSynced => _playbackRateSynced;
        public GameObject SpawnedInstance => _spawnedInstance;

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

        #region 运行时生命周期 (由 MontagePlayer 调度)

        public override bool CanEnter(in MontageActionContext context)
        {
            return base.CanEnter(context) && _vfxPrefab != null;
        }

        public override void OnEnter(in MontageActionContext context)
        {
            base.OnEnter(context);

            if (_vfxPrefab == null)
            {
                return;
            }

            // 1. 基类解析挂点与计算空间变换
            ResolveTargetBone(context);
            CalculateWorldTransform(out Vector3 worldPos, out Quaternion worldRot);
            Transform attachParent = GetSpawnParent(context, isPreview: false);

            // 2. 从运行时对象池取出实例
            _spawnedInstance = MontageObjectPool.Spawn(
                _vfxPrefab,
                worldPos,
                worldRot,
                attachParent,
                1.0f);

            if (_spawnedInstance == null)
            {
                return;
            }

            _spawnedInstance.transform.localScale = Vector3.Scale(_vfxPrefab.transform.localScale, Scale);

            // 3. 初始化并驱动 ParticleSystem（自适应 Block 长度的时间缩放倍率）
            _cachedParticleSystems = _spawnedInstance.GetComponentsInChildren<ParticleSystem>(true);
            float speedScale = GetStretchSpeedMultiplier();
            float simRate = speedScale * (_playbackRateSynced ? Mathf.Max(0.001f, context.PlaybackRate) : 1.0f);

            for (int i = 0; i < _cachedParticleSystems.Length; i++)
            {
                var ps = _cachedParticleSystems[i];
                var main = ps.main;
                main.simulationSpeed = simRate;
                ps.Play(true);
            }
        }

        public override void OnUpdate(in MontageActionContext context, float deltaTime)
        {
            if (_spawnedInstance == null)
            {
                return;
            }

            // 1. 基类统一处理播放中世界位置/旋转的动态跟随与更新
            UpdateSpatialTransform(_spawnedInstance, in context);

            if (_cachedParticleSystems == null || _cachedParticleSystems.Length == 0)
            {
                return;
            }

            // 2. 同步自适应缩放与播放速率变化
            float speedScale = GetStretchSpeedMultiplier();
            float simRate = speedScale * (_playbackRateSynced ? Mathf.Max(0.001f, context.PlaybackRate) : 1.0f);
            for (int i = 0; i < _cachedParticleSystems.Length; i++)
            {
                var ps = _cachedParticleSystems[i];
                if (ps != null)
                {
                    var main = ps.main;
                    main.simulationSpeed = simRate;
                }
            }
        }

        public override void OnExit(in MontageActionContext context)
        {
            if (_spawnedInstance != null)
            {
                if (_cachedParticleSystems != null)
                {
                    for (int i = 0; i < _cachedParticleSystems.Length; i++)
                    {
                        var ps = _cachedParticleSystems[i];
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

                MontageObjectPool.Recycle(_spawnedInstance);
                _spawnedInstance = null;
            }

            _cachedParticleSystems = null;
            base.OnExit(context);
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

            if (_vfxPrefab == null)
            {
                return;
            }

            // 1. 基类解析挂点与计算空间变换
            ResolveTargetBone(context);
            CalculateWorldTransform(out Vector3 worldPos, out Quaternion worldRot);
            Transform attachParent = GetSpawnParent(context, isPreview: true);
            if (attachParent == null && context.TargetObject != null)
            {
                attachParent = context.TargetObject.transform;
            }

            // 2. 在私有视口场景锚点下即时实例化，避免跨场景对象池污染
            _spawnedInstance = UnityEngine.Object.Instantiate(
                _vfxPrefab,
                worldPos,
                worldRot,
                attachParent);

            if (_spawnedInstance == null)
            {
                return;
            }

            _spawnedInstance.name = _vfxPrefab.name;
            _spawnedInstance.hideFlags = HideFlags.HideAndDontSave;
            _spawnedInstance.transform.localScale = Vector3.Scale(_vfxPrefab.transform.localScale, Scale);

            // 3. 初始化粒子系统为初始状态（暂停待步进，应用时间缩放）
            _cachedParticleSystems = _spawnedInstance.GetComponentsInChildren<ParticleSystem>(true);
            float speedScale = GetStretchSpeedMultiplier();
            float simRate = speedScale * (_playbackRateSynced ? Mathf.Max(0.001f, context.PlaybackRate) : 1.0f);

            for (int i = 0; i < _cachedParticleSystems.Length; i++)
            {
                var ps = _cachedParticleSystems[i];
                var main = ps.main;
                main.simulationSpeed = simRate;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Clear(true);
                ps.Play(true);
            }
        }

        public override void OnPreviewUpdate(in MontageActionContext context, float deltaTime)
        {
            if (_spawnedInstance == null)
            {
                return;
            }

            // 1. 动态位置追踪与更新
            UpdateSpatialTransform(_spawnedInstance, in context);

            if (_cachedParticleSystems == null || _cachedParticleSystems.Length == 0)
            {
                return;
            }

            // 2. 同步模拟速率
            float speedScale = GetStretchSpeedMultiplier();
            float simRate = speedScale * (_playbackRateSynced ? Mathf.Max(0.001f, context.PlaybackRate) : 1.0f);
            for (int i = 0; i < _cachedParticleSystems.Length; i++)
            {
                var ps = _cachedParticleSystems[i];
                if (ps != null)
                {
                    var main = ps.main;
                    main.simulationSpeed = simRate;
                }
            }

            // 3. 编辑器非运行模式下，手动驱动粒子自适应仿真
            float effectiveStep = deltaTime * speedScale * (_playbackRateSynced ? context.PlaybackRate : 1.0f);
            if (effectiveStep > 0.0001f)
            {
                for (int i = 0; i < _cachedParticleSystems.Length; i++)
                {
                    var ps = _cachedParticleSystems[i];
                    if (ps != null)
                    {
                        ps.Simulate(effectiveStep, true, false);
                    }
                }
            }
        }

        public override void OnPreviewExit(in MontageActionContext context)
        {
            if (_spawnedInstance != null)
            {
                if (_cachedParticleSystems != null)
                {
                    for (int i = 0; i < _cachedParticleSystems.Length; i++)
                    {
                        var ps = _cachedParticleSystems[i];
                        if (ps != null)
                        {
                            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                            ps.Clear(true);
                        }
                    }
                }

                UnityEngine.Object.DestroyImmediate(_spawnedInstance);
                _spawnedInstance = null;
            }

            _cachedParticleSystems = null;
            base.OnPreviewExit(context);
        }

        /// <summary>
        /// 将粒子系统精准模拟到指定的时间点（编辑器非播放态 Scrub / Seek / 定格专用）。
        /// </summary>
        /// <param name="targetLocalTime">目标时间点（秒）</param>
        public void SimulateToTime(float targetLocalTime)
        {
            if (_cachedParticleSystems == null || _cachedParticleSystems.Length == 0)
            {
                return;
            }

            targetLocalTime = Mathf.Max(0f, targetLocalTime);
            for (int i = 0; i < _cachedParticleSystems.Length; i++)
            {
                var ps = _cachedParticleSystems[i];
                if (ps != null)
                {
                    // restart = true: 从0重新模拟到 targetLocalTime，实现任意时刻绝对一致的确定性粒子切片
                    ps.Simulate(targetLocalTime, true, true, true);
                }
            }
        }

        public override void OnPreviewScrub(in MontageActionContext context, float localTime)
        {
            if (_spawnedInstance == null)
            {
                return;
            }

            // 1. 同步空间位置
            UpdateSpatialTransform(_spawnedInstance, in context);

            // 2. 将动作块内部局部时间归一化到 [0, 1] 进度，再精确线性映射到用户截取的有效区间 [ClipStartTime, ClipEndTime]
            float blockDur = Mathf.Max(0.0001f, BlockDuration);
            float progress = Mathf.Clamp01(localTime / blockDur);
            float targetTime = ClipStartTime + progress * EffectiveClipDuration;

            // 3. 绝对时间精确模拟粒子切片
            SimulateToTime(targetTime);
        }

        public override void OnPreviewParametersChanged(in MontageActionContext context)
        {
            if (_spawnedInstance == null)
            {
                return;
            }

            // 1. 原地更新空间变换与挂点骨骼，绝对不重新销毁或清空粒子
            UpdatePreviewTransform(_spawnedInstance, in context);

            // 2. 同步局部缩放倍率
            if (_vfxPrefab != null)
            {
                _spawnedInstance.transform.localScale = Vector3.Scale(_vfxPrefab.transform.localScale, Scale);
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

        #region 时长分析与时间拉伸辅助计算

        /// <summary>
        /// 获取特效预制件中所有子粒子系统完整消散完毕的最大自然时长（秒）。
        /// </summary>
        public float GetNaturalDuration()
        {
            if (_cachedNaturalDuration > 0.001f)
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

        /// <summary>
        /// 获取基于截取有效区间与当前动作块时长计算出的时间拉伸速率倍率（EffectiveClipDuration / BlockDuration）。
        /// 兼容保留此方法，内部直接调用基类统一契约属性 SpeedMultiplier。
        /// </summary>
        public float GetStretchSpeedMultiplier()
        {
            return SpeedMultiplier;
        }

        /// <summary>
        /// 递归深度扫描预制件层级下所有 ParticleSystem，计算从首颗粒子发射到最后一颗残余粒子彻底消散完毕的最大绝对耗时。
        /// 耗时 = startDelay + duration + startLifetime
        /// </summary>
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

            // 优先使用非循环粒子的完整消散总时长；若整个预制体全部为循环粒子，则使用单圈循环周期
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
