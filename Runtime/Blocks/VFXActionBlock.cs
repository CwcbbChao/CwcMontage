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

        #endregion

        #region 私有非序列化运行时字段

        [NonSerialized] private GameObject _spawnedInstance;
        [NonSerialized] private ParticleSystem[] _cachedParticleSystems;

        #endregion

        #region 公共属性

        public GameObject VFXPrefab => _vfxPrefab;
        public MontageVFXStopBehavior StopBehavior => _stopBehavior;
        public bool PlaybackRateSynced => _playbackRateSynced;

        #endregion

        #region 动作块生命周期

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
            Transform attachParent = GetSpawnParent(context);

            // 2. 从对象池取出实例
            _spawnedInstance = MontageObjectPool.Spawn(
                _vfxPrefab,
                worldPos,
                worldRot,
                attachParent,
                1.0f,
                context.IsPreview);

            if (_spawnedInstance == null)
            {
                return;
            }

            _spawnedInstance.transform.localScale = Vector3.Scale(_vfxPrefab.transform.localScale, Scale);

            // 3. 初始化并驱动 ParticleSystem
            _cachedParticleSystems = _spawnedInstance.GetComponentsInChildren<ParticleSystem>(true);
            float simRate = _playbackRateSynced ? Mathf.Max(0.001f, context.PlaybackRate) : 1.0f;

            for (int i = 0; i < _cachedParticleSystems.Length; i++)
            {
                var ps = _cachedParticleSystems[i];
                var main = ps.main;
                main.simulationSpeed = simRate;

                if (!context.IsPreview)
                {
                    ps.Play(true);
                }
                else
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ps.Clear(true);
                    ps.Play(true);
                }
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

            // 2. 同步播放速率变化
            if (_playbackRateSynced)
            {
                float simRate = Mathf.Max(0.001f, context.PlaybackRate);
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

            // 3. 编辑器非运行模式下，手动驱动粒子仿真，实现 3D 视口所见即所得
            if (context.IsPreview)
            {
                float effectiveStep = deltaTime * (_playbackRateSynced ? context.PlaybackRate : 1.0f);
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

                MontageObjectPool.Recycle(_spawnedInstance, context.IsPreview);
                _spawnedInstance = null;
            }

            _cachedParticleSystems = null;
            base.OnExit(context);
        }

        #endregion
    }
}
