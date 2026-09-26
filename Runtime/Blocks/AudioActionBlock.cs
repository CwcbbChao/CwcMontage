using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 音效播放模式。
    /// </summary>
    public enum MontageAudioPlayMode
    {
        /// <summary>
        /// 单次触发播放（One-shot，触发后播放完整音频片段）。
        /// </summary>
        OneShot,

        /// <summary>
        /// 块区间内持续循环播放（在时间轴区间内 Loop，并在离开区间时停止或淡出）。
        /// </summary>
        LoopDuringBlock
    }

    /// <summary>
    /// 音效表现动作块（AudioActionBlock）。
    /// 继承自 MontageActionBlockBase<AudioState>，运行时 0-GC 且资产完全只读化。
    /// 支持单音频与多音频随机池、3D 空间衰减、音高随机偏差与单次/循环淡出控制，
    /// 全面支持运行时专用通道池管理与编辑器 3D 视口非运行模式下的音频即时预览。
    /// </summary>
    [Serializable]
    [MontageCategory("Audio")]
    [MontageDisplayName("Play Audio")]
    [MontageColor("#1E90FF")]
    public class AudioActionBlock : MontageActionBlockBase<AudioActionBlock.AudioState>
    {
        #region 内部状态定义 (纯运行时持有，随槽位复用)

        /// <summary>
        /// 音效动作块运行时动态状态。
        /// </summary>
        public class AudioState : IMontageBlockState
        {
            public AudioSource ActiveAudioSource;
            public AudioClip SelectedClip;
            public float RuntimeVolume;

            public void Reset()
            {
                ActiveAudioSource = null;
                SelectedClip = null;
                RuntimeVolume = 0f;
            }
        }

        #endregion

        #region 公共静态预览委托 (由 Editor 模块注入)

        public static Action<AudioClip, bool> PreviewAudioPlayHandler;
        public static Action PreviewAudioStopHandler;

        #endregion

        #region Inspector 字段

        [Header("Audio Clip Sources")]
        [Tooltip("基础音频片段。")]
        [SerializeField] private AudioClip _audioClip;

        [Tooltip("随机音频片段候选列表（若配置，将从列表中随机选取一个播放以增加打击音效丰富度）。")]
        [SerializeField] private List<AudioClip> _randomAudioClips = new();

        [Header("Volume & Pitch")]
        [Range(0f, 1f)]
        [Tooltip("基础音量大小。")]
        [SerializeField] private float _volume = 1.0f;

        [Range(0.1f, 3f)]
        [Tooltip("基础音调倍率。")]
        [SerializeField] private float _pitch = 1.0f;

        [Range(0f, 0.5f)]
        [Tooltip("音调随机微调范围（在 [pitch - offset, pitch + offset] 内随机，消除机械重复感）。")]
        [SerializeField] private float _randomPitchOffset = 0.05f;

        [Header("Spatial Blend & 3D Settings")]
        [Range(0f, 1f)]
        [Tooltip("空间混合权重（0 为纯 2D 界面/环境音，1 为纯 3D 空间定位音）。")]
        [SerializeField] private float _spatialBlend = 1.0f;

        [Min(0.01f)]
        [Tooltip("3D 音频最小衰减距离。")]
        [SerializeField] private float _minDistance = 1.0f;

        [Min(0.1f)]
        [Tooltip("3D 音频最大传播距离。")]
        [SerializeField] private float _maxDistance = 35.0f;

        [Header("Playback Mode & Fade")]
        [Tooltip("播放模式：单次触发或在动作块区间内循环。")]
        [SerializeField] private MontageAudioPlayMode _playMode = MontageAudioPlayMode.OneShot;

        [Tooltip("在离开动作块或被打断时是否执行平滑淡出。")]
        [SerializeField] private bool _fadeOutOnExit = true;

        [Range(0.01f, 1.0f)]
        [Tooltip("淡出持续时间（秒）。")]
        [SerializeField] private float _fadeOutDuration = 0.1f;

        [Header("Attachment")]
        [Tooltip("音频空间定位挂点（精简为 8 个人形核心大骨骼，用于 3D 空间音效发声点）。")]
        [SerializeField] private MontageTargetBone _targetBone = MontageTargetBone.Root;

        #endregion

        #region 公共属性

        public AudioClip AudioClip => _audioClip;
        public IReadOnlyList<AudioClip> RandomAudioClips => _randomAudioClips;
        public float Volume => _volume;
        public float Pitch => _pitch;
        public float RandomPitchOffset => _randomPitchOffset;
        public float SpatialBlend => _spatialBlend;
        public float MinDistance => _minDistance;
        public float MaxDistance => _maxDistance;
        public MontageAudioPlayMode PlayMode => _playMode;
        public bool FadeOutOnExit => _fadeOutOnExit;
        public float FadeOutDuration => _fadeOutDuration;
        public MontageTargetBone TargetBone => _targetBone;

        #endregion

        #region 运行时生命周期 (强类型 0-GC 调度)

        public override bool CanEnter(in MontageActionContext context)
        {
            if (!base.CanEnter(context)) return false;
            return _audioClip != null || (_randomAudioClips != null && _randomAudioClips.Count > 0);
        }

        protected override void OnEnter(in MontageActionContext context, AudioState state)
        {
            state.SelectedClip = SelectAudioClip();
            if (state.SelectedClip == null)
            {
                return;
            }

            var boneTransform = context.GetTargetBone(_targetBone);
            Vector3 spawnPos = boneTransform != null ? boneTransform.position : context.TargetObject.transform.position;

            state.ActiveAudioSource = MontageObjectPool.GetAudioSource(spawnPos, null);
            if (state.ActiveAudioSource == null)
            {
                return;
            }

            float finalPitch = _pitch;
            if (_randomPitchOffset > 0.001f)
            {
                finalPitch += UnityEngine.Random.Range(-_randomPitchOffset, _randomPitchOffset);
            }
            finalPitch = Mathf.Clamp(finalPitch, 0.1f, 3.0f);

            state.RuntimeVolume = Mathf.Clamp01(_volume);
            state.ActiveAudioSource.clip = state.SelectedClip;
            state.ActiveAudioSource.volume = state.RuntimeVolume;
            state.ActiveAudioSource.pitch = finalPitch;
            state.ActiveAudioSource.spatialBlend = _spatialBlend;
            state.ActiveAudioSource.minDistance = _minDistance;
            state.ActiveAudioSource.maxDistance = _maxDistance;
            state.ActiveAudioSource.loop = (_playMode == MontageAudioPlayMode.LoopDuringBlock);
            state.ActiveAudioSource.rolloffMode = AudioRolloffMode.Logarithmic;

            state.ActiveAudioSource.Play();
        }

        protected override void OnUpdate(in MontageActionContext context, AudioState state, float deltaTime)
        {
            if (state.ActiveAudioSource != null && _spatialBlend > 0.01f)
            {
                var boneTransform = context.GetTargetBone(_targetBone);
                if (boneTransform != null)
                {
                    state.ActiveAudioSource.transform.position = boneTransform.position;
                }
            }

            if (_playMode == MontageAudioPlayMode.OneShot && state.ActiveAudioSource != null)
            {
                if (!state.ActiveAudioSource.isPlaying)
                {
                    MontageObjectPool.RecycleAudioSource(state.ActiveAudioSource);
                    state.ActiveAudioSource = null;
                }
            }
        }

        protected override void OnExit(in MontageActionContext context, AudioState state)
        {
            if (state.ActiveAudioSource != null)
            {
                MontageObjectPool.RecycleAudioSource(state.ActiveAudioSource);
                state.ActiveAudioSource = null;
            }

            state.SelectedClip = null;
        }

        #endregion

        #region 编辑器视口预览生命周期 (由 MontageEditorUI 调度)

        public override bool CanPreviewEnter(in MontageActionContext context)
        {
            if (!base.CanPreviewEnter(context)) return false;
            return _audioClip != null || (_randomAudioClips != null && _randomAudioClips.Count > 0);
        }

        public override void OnPreviewEnter(in MontageActionContext context)
        {
            base.OnPreviewEnter(context);

            var clip = SelectAudioClip();
            if (clip == null) return;

            PreviewAudioPlayHandler?.Invoke(
                clip,
                _playMode == MontageAudioPlayMode.LoopDuringBlock);
        }

        public override void OnPreviewExit(in MontageActionContext context)
        {
            base.OnPreviewExit(context);
            PreviewAudioStopHandler?.Invoke();
        }

        public override bool RequiresPreviewRecreate(MontageActionBlockBase newBlock)
        {
            if (newBlock is AudioActionBlock newAudio)
            {
                if (_audioClip != newAudio._audioClip)
                {
                    return true;
                }
            }
            return false;
        }

        public override string GetTimingCustomHint()
        {
            if (_audioClip == null && (_randomAudioClips == null || _randomAudioClips.Count == 0)) return null;
            string clipName = _audioClip != null ? _audioClip.name : "Random";
            float dur = _audioClip != null ? _audioClip.length : 0f;
            return $"Clip: {clipName} ({dur:F2}s)";
        }

        #endregion

        #region 私有辅助方法

        private AudioClip SelectAudioClip()
        {
            if (_randomAudioClips != null && _randomAudioClips.Count > 0)
            {
                int validCount = 0;
                for (int i = 0; i < _randomAudioClips.Count; i++)
                {
                    if (_randomAudioClips[i] != null) validCount++;
                }

                if (validCount > 0)
                {
                    int randIndex = UnityEngine.Random.Range(0, _randomAudioClips.Count);
                    var candidate = _randomAudioClips[randIndex];
                    if (candidate != null) return candidate;
                }
            }

            return _audioClip;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticEventsOnEnterPlayMode()
        {
            PreviewAudioPlayHandler = null;
            PreviewAudioStopHandler = null;
        }

        #endregion
    }
}
