using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// Audio playback trigger mode.
    /// </summary>
    public enum MontageAudioPlayMode
    {
        /// <summary>
        /// Plays the audio clip once to completion (One-shot).
        /// </summary>
        OneShot,

        /// <summary>
        /// Loops audio within the block range and stops/fades out when exiting.
        /// </summary>
        LoopDuringBlock
    }

    /// <summary>
    /// Action block for triggering and controlling 2D/3D audio playback on a character.
    /// Supports single clips or randomized pools, 3D spatial attenuation, pitch variation, and smooth fade-outs.
    /// </summary>
    [Serializable]
    [MontageCategory("Audio")]
    [MontageDisplayName("Play Audio")]
    [MontageColor("#1E90FF")]
    public class AudioActionBlock : MontageActionBlockBase<AudioActionBlock.AudioState>
    {
        #region Internal State Definition

        /// <summary>
        /// Runtime dynamic state for AudioActionBlock.
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

        #region Static Preview Delegates (Injected by Editor)

        public static Action<AudioClip, bool> PreviewAudioPlayHandler;
        public static Action PreviewAudioStopHandler;

        #endregion

        #region Serialized Fields

        [Header("Audio Clip Sources")]
        [Tooltip("Base audio clip asset.")]
        [SerializeField] private AudioClip _audioClip;

        [Tooltip("Optional list of audio clips for randomized selection.")]
        [SerializeField] private List<AudioClip> _randomAudioClips = new();

        [Header("Volume & Pitch")]
        [Range(0f, 1f)]
        [Tooltip("Base audio volume.")]
        [SerializeField] private float _volume = 1.0f;

        [Range(0.1f, 3f)]
        [Tooltip("Base pitch multiplier.")]
        [SerializeField] private float _pitch = 1.0f;

        [Range(0f, 0.5f)]
        [Tooltip("Random pitch deviation range [pitch - offset, pitch + offset].")]
        [SerializeField] private float _randomPitchOffset = 0.05f;

        [Header("Spatial Blend & 3D Settings")]
        [Range(0f, 1f)]
        [Tooltip("Spatial blend: 0 for 2D, 1 for full 3D spatial positioning.")]
        [SerializeField] private float _spatialBlend = 1.0f;

        [Min(0.01f)]
        [Tooltip("3D audio minimum attenuation distance.")]
        [SerializeField] private float _minDistance = 1.0f;

        [Min(0.1f)]
        [Tooltip("3D audio maximum attenuation distance.")]
        [SerializeField] private float _maxDistance = 35.0f;

        [Header("Playback Mode & Fade")]
        [Tooltip("Playback mode: OneShot or LoopDuringBlock.")]
        [SerializeField] private MontageAudioPlayMode _playMode = MontageAudioPlayMode.OneShot;

        [Tooltip("Fades out audio when leaving the block or when interrupted.")]
        [SerializeField] private bool _fadeOutOnExit = true;

        [Range(0.01f, 1.0f)]
        [Tooltip("Fade-out duration in seconds.")]
        [SerializeField] private float _fadeOutDuration = 0.1f;

        [Header("Attachment")]
        [Tooltip("Bone attachment for 3D positional audio.")]
        [SerializeField] private MontageTargetBone _targetBone = MontageTargetBone.Root;

        #endregion

        #region Properties

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

        #region Runtime Lifecycle (0-GC Typed Dispatch)

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

        #region Editor Preview Lifecycle

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

        #region Private Helper Methods

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
