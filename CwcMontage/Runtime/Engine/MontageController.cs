using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 模型骨骼适配类型。
    /// 用于区分当前模型为通用人形骨骼还是特异性泛型/非人形骨骼。
    /// </summary>
    public enum MontageSkeletonType
    {
        [Tooltip("人形骨骼：支持播放外部传入的通用人形蒙太奇动作")]
        Humanoid = 0,

        [Tooltip("泛型/非人形骨骼：四足、异形等特异骨骼。外部传入通用人形动作时将被自动拦截安全退出")]
        Generic = 1
    }

    /// <summary>
    /// Configuration data for an individual animation layer.
    /// </summary>
    [Serializable]
    public struct MontageLayerConfig
    {
        [Tooltip("Avatar mask applied to this layer.")]
        public AvatarMask AvatarMask;

        [Tooltip("Whether this layer uses additive blending.")]
        public bool IsAdditive;

        [Range(0f, 1f)]
        [Tooltip("Base weight multiplier for this layer.")]
        public float LayerWeight;
    }

    /// <summary>
    /// Central controller component for playing and managing animation montages on a character.
    /// Builds a multi-layer Playables mixer topology (Locomotion, UpperBody, FullBody, Additive)
    /// and manages playback, CrossFade transitions, and Root Motion delegation.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Cwcbb/Montage/Montage Controller")]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(
        autoUpdateAPI: true,
        sourceNamespace: "Cwcbb.Tools.CwcMontage",
        sourceAssembly: null,
        sourceClassName: "MontageCoordinator")]
    public class MontageController : MonoBehaviour
    {
        #region Internal Data Structures

        protected enum SlotTransitionState
        {
            Inactive = 0,
            FadingIn = 1,
            Active = 2,
            FadingOut = 3
        }

        protected class SlotState
        {
            public readonly int SlotIndex;
            public AnimationMixerPlayable SlotMixer;
            public readonly List<AnimationClipPlayable> SegmentPlayables = new();
            public MontagePlayer Player;
            public float Weight;
            public int Generation { get; set; } = 1;
            public SlotTransitionState TransitionState { get; set; } = SlotTransitionState.Inactive;
            public float TransitionDuration { get; set; } = 0.1f;
            public float TransitionElapsed { get; set; } = 0.0f;
            public bool HasActiveAnimation { get; set; }
            public float CurrentSegmentWeightMultiplier { get; set; } = 1.0f;
            public float TransitionStartWeight { get; set; } = 1.0f;
            public bool IsHoldingPoseOnFinish { get; set; }

            public readonly List<int> TempEvalIndices = new();
            public readonly List<float> TempEvalTimes = new();
            public readonly List<float> TempEvalWeights = new();

            public bool IsOccupied => Player != null;

            public SlotState(int slotIndex)
            {
                SlotIndex = slotIndex;
            }

            public void Bind(MontagePlayer player, float blendInDuration)
            {
                Generation++;
                if (Generation <= 0) Generation = 1;

                Player = player;
                TransitionDuration = Mathf.Max(0.0001f, blendInDuration);
                TransitionElapsed = 0.0f;
                TransitionStartWeight = 0.0f;
                HasActiveAnimation = false;
                IsHoldingPoseOnFinish = false;
                CurrentSegmentWeightMultiplier = 1.0f;

                if (blendInDuration > 0.0001f)
                {
                    Weight = 0.0f;
                    TransitionState = SlotTransitionState.FadingIn;
                }
                else
                {
                    Weight = 1.0f;
                    TransitionState = SlotTransitionState.Active;
                }
            }

            public void StartFadeOut(float blendOutDuration)
            {
                if (!IsOccupied) return;

                float currentEffective = HasActiveAnimation ? (Weight * CurrentSegmentWeightMultiplier) : 0f;
                TransitionStartWeight = currentEffective;
                TransitionDuration = Mathf.Max(0.0001f, blendOutDuration);
                TransitionElapsed = 0f;
                IsHoldingPoseOnFinish = true;

                if (currentEffective > 0.0001f && blendOutDuration > 0.0001f)
                {
                    Weight = currentEffective;
                    TransitionState = SlotTransitionState.FadingOut;
                }
                else
                {
                    Weight = 0f;
                    TransitionState = SlotTransitionState.Inactive;
                }

                if (Player != null && Player.IsPlaying)
                {
                    Player.Stop(blendOutDuration);
                }
            }

            public void TickTransition(float deltaTime)
            {
                if (!IsOccupied)
                {
                    Weight = 0f;
                    TransitionState = SlotTransitionState.Inactive;
                    return;
                }

                switch (TransitionState)
                {
                    case SlotTransitionState.FadingIn:
                        TransitionElapsed += deltaTime;
                        if (TransitionDuration <= 0.0001f || TransitionElapsed >= TransitionDuration)
                        {
                            Weight = 1.0f;
                            TransitionState = SlotTransitionState.Active;
                        }
                        else
                        {
                            Weight = Mathf.Clamp01(TransitionElapsed / TransitionDuration);
                        }
                        break;

                    case SlotTransitionState.Active:
                        Weight = 1.0f;
                        break;

                    case SlotTransitionState.FadingOut:
                        TransitionElapsed += deltaTime;
                        if (TransitionDuration <= 0.0001f || TransitionElapsed >= TransitionDuration)
                        {
                            Weight = 0.0f;
                            TransitionState = SlotTransitionState.Inactive;
                        }
                        else
                        {
                            float alpha = Mathf.Clamp01(TransitionElapsed / TransitionDuration);
                            Weight = TransitionStartWeight * (1.0f - alpha);
                            if (Weight <= 0.0001f)
                            {
                                Weight = 0.0f;
                                TransitionState = SlotTransitionState.Inactive;
                            }
                        }
                        break;

                    case SlotTransitionState.Inactive:
                    default:
                        Weight = 0.0f;
                        break;
                }
            }

            public void DestroyPlayables(PlayableGraph graph, AnimationMixerPlayable parentMixer)
            {
                if (parentMixer.IsValid())
                {
                    parentMixer.DisconnectInput(SlotIndex);
                }

                for (int i = 0; i < SegmentPlayables.Count; i++)
                {
                    var cp = SegmentPlayables[i];
                    if (cp.IsValid() && graph.IsValid())
                    {
                        if (SlotMixer.IsValid())
                        {
                            SlotMixer.DisconnectInput(i);
                        }
                        graph.DestroyPlayable(cp);
                    }
                }
                SegmentPlayables.Clear();

                if (SlotMixer.IsValid() && graph.IsValid())
                {
                    graph.DestroyPlayable(SlotMixer);
                    SlotMixer = default;
                }
            }

            public void Reset()
            {
                Player = null;
                Weight = 0f;
                TransitionState = SlotTransitionState.Inactive;
                TransitionDuration = 0.1f;
                TransitionElapsed = 0.0f;
                HasActiveAnimation = false;
                CurrentSegmentWeightMultiplier = 1.0f;
                TransitionStartWeight = 1.0f;
                IsHoldingPoseOnFinish = false;
                TempEvalIndices.Clear();
                TempEvalTimes.Clear();
                TempEvalWeights.Clear();
            }
        }

        protected class LayerRuntimeState
        {
            public readonly MontageLayerChannel Channel;
            public readonly int LayerIndex; // 0: UpperBody, 1: FullBody, 2: Additive
            public readonly int TopLevelInputIndex; // 1, 2, 3
            public AnimationMixerPlayable LayerMixer;

            public readonly SlotState Slot0 = new(0);
            public readonly SlotState Slot1 = new(1);
            public int ActiveSlotIndex = -1;
            public float LayerWeight = 1.0f;

            public LayerRuntimeState(MontageLayerChannel channel, int layerIndex, int topLevelInputIndex)
            {
                Channel = channel;
                LayerIndex = layerIndex;
                TopLevelInputIndex = topLevelInputIndex;
            }
        }

        #endregion

        #region Serialized Fields

        [Header("Animator Binding")]
        [Tooltip("Target Animator component. Automatically searches self and children if not assigned.")]
        [SerializeField] private Animator _animator;

        [Header("Humanoid Layer Settings")]
        [Tooltip("Optional custom AvatarMask for the upper-body layer (uses default humanoid upper-body mask if empty).")]
        [SerializeField] private AvatarMask _customUpperBodyMask;

        [Header("Playback Defaults")]
        [Tooltip("Global playback speed multiplier.")]
        [Min(0.0001f)]
        [SerializeField] private float _globalPlaybackRate = 1.0f;

        [Header("Playables Integration")]
        [Tooltip("Whether to automatically call Evaluate on the PlayableGraph during Update.")]
        [SerializeField] private bool _autoEvaluateGraph = true;

        #endregion

        #region Protected & Private Fields

        protected PlayableGraph _playableGraph;
        protected AnimationLayerMixerPlayable _topLevelMixer;
        protected Playable _locomotionPlayable;
        protected RuntimeAnimatorController _originalController;
        protected MontageAnimatorDispatcher _animatorDispatcher;
        protected MontageSkeletonType _skeletonType = MontageSkeletonType.Humanoid;

        protected readonly List<LayerRuntimeState> _layerStates = new(3);
        protected readonly Transform[] _cachedBones = new Transform[MontageBoneUtility.BONE_COUNT];
        protected readonly HashSet<MontagePlayer> _tickablePlayers = new(4);
        protected readonly List<MontagePlayer> _playerPool = new(3);
        protected readonly List<MontagePlayer> _activePlayers = new(3);
        protected readonly Dictionary<int, (MontagePlayer player, int generation)> _standalonePlayers = new();
        protected int _nextStandaloneId = 1;
        protected IMontageRootMotionReceiver _cachedReceiver;

        protected bool _isGraphInitialized;

        #endregion

        #region Properties

        /// <summary>
        /// The bound Animator component.
        /// </summary>
        public Animator TargetAnimator => _animator;

        /// <summary>
        /// 当前绑定的 Animator 骨骼适配类型（自动识别：若为人形骨骼则为 Humanoid，否则为 Generic）。
        /// </summary>
        public virtual MontageSkeletonType SkeletonType => DetectSkeletonType();

        /// <summary>
        /// 当前是否为人形骨骼。
        /// </summary>
        public virtual bool IsHumanoid => SkeletonType == MontageSkeletonType.Humanoid;

        /// <summary>
        /// Whether any layer is currently playing a montage (including fade-in, steady playback, and fade-out phases).
        /// </summary>
        public bool IsPlayingMontage => IsPlayingAnyMontage;

        /// <summary>
        /// Whether any layer is currently playing a montage.
        /// </summary>
        public bool IsPlayingAnyMontage
        {
            get
            {
                for (int i = 0; i < _layerStates.Count; i++)
                {
                    if (IsPlayingLayer(i)) return true;
                }
                return false;
            }
        }

        /// <summary>
        /// The active montage playback handle on the primary layer (FullBody first, then UpperBody/Additive).
        /// </summary>
        public MontageHandle ActiveHandle
        {
            get
            {
                var h = GetActiveHandle(MontageLayerChannel.FullBody);
                if (h.IsValid) return h;
                h = GetActiveHandle(MontageLayerChannel.UpperBody);
                if (h.IsValid) return h;
                return GetActiveHandle(MontageLayerChannel.Additive);
            }
        }

        /// <summary>
        /// Global playback rate multiplier.
        /// </summary>
        public float GlobalPlaybackRate
        {
            get => _globalPlaybackRate;
            set => _globalPlaybackRate = Mathf.Max(0.0001f, value);
        }

        /// <summary>
        /// Root Playable node of the top-level layer mixer, so external Playables systems (such as Animancer) can attach the whole montage to their own root graph as a layer.
        /// </summary>
        public Playable RootPlayable => _topLevelMixer;

        /// <summary>
        /// Whether this coordinator automatically calls Evaluate during Update to advance graph sampling.
        /// </summary>
        public bool AutoEvaluateGraph
        {
            get => _autoEvaluateGraph;
            set => _autoEvaluateGraph = value;
        }

        #endregion

        #region Protected Properties

        /// <summary>
        /// Whether PlayableGraph.Evaluate is executed during this frame's Update. Subclasses can override this property to dynamically decide whether this coordinator advances graph sampling.
        /// </summary>
        protected virtual bool ShouldEvaluateGraph => _autoEvaluateGraph;

        /// <summary>
        /// The underlying runtime PlayableGraph instance.
        /// </summary>
        protected PlayableGraph Graph => _playableGraph;

        /// <summary>
        /// The top-level layer mixer.
        /// </summary>
        protected AnimationLayerMixerPlayable TopLevelMixer => _topLevelMixer;

        /// <summary>
        /// Runtime state entries for all layers.
        /// </summary>
        protected IReadOnlyList<LayerRuntimeState> LayerStates => _layerStates;

        /// <summary>
        /// The bound Animator component.
        /// </summary>
        protected Animator AnimatorComponent => _animator;

        #endregion

        #region Events

        /// <summary>
        /// Triggered when the dispatcher computes the effective root motion displacement and rotation delta for the current frame.
        /// External movement systems (such as character controllers) can subscribe to consume the physical displacement and apply secondary filtering.
        /// </summary>
        public event Action<Vector3, Quaternion> OnRootMotionDelta;

        /// <summary>
        /// Triggered when a montage starts playing, providing the corresponding playback handle.
        /// </summary>
        public event Action<MontageHandle> OnMontageStarted;

        /// <summary>
        /// Triggered when a montage finishes or is interrupted, providing the corresponding playback handle.
        /// </summary>
        public event Action<MontageHandle> OnMontageEnded;

        /// <summary>
        /// Triggered when a montage crosses a physical section boundary, providing the corresponding playback handle and the target section index.
        /// </summary>
        public event Action<MontageHandle, int> OnSectionChanged;

        #endregion

        #region Unity Lifecycle

        protected virtual void Awake()
        {
            EnsureAnimator();
            _cachedReceiver = GetComponent<IMontageRootMotionReceiver>();
            InitializePlayableGraph();
        }

        protected virtual void Update()
        {
            if (!_isGraphInitialized || !_playableGraph.IsValid())
            {
                return;
            }

            float effectiveDelta = Time.deltaTime * _globalPlaybackRate;

            // 1. Update the double-buffered slot players and CrossFade weights in each action layer first.
            UpdateLayers(effectiveDelta);

            // 2. Then manually advance PlayableGraph sampling (drives child Animator sampling and dispatches Root Motion in OnAnimatorMove).
            if (ShouldEvaluateGraph)
            {
                _playableGraph.Evaluate(effectiveDelta);
            }
        }

        protected virtual void LateUpdate()
        {
            if (!_isGraphInitialized || !_playableGraph.IsValid())
            {
                return;
            }

            // Upper-body reference bone pose correction decoupled from the root (runs after all animation evaluation for the frame; removes lower-body pelvis tilt and sway while running and restores the source animation's true facing for side-turn and straight-thrust motions).
            ApplyUpperBodySpineDecoupling();
        }

        protected virtual void OnDestroy()
        {
            if (_animatorDispatcher != null)
            {
                _animatorDispatcher.Unbind();
            }

            // 强制终止所有图层槽位中的播放器，确保动作块安全退出并回收
            for (int i = 0; i < _layerStates.Count; i++)
            {
                var layer = _layerStates[i];
                EvictSlot(layer, layer.Slot0);
                EvictSlot(layer, layer.Slot1);
            }

            for (int i = 0; i < _playerPool.Count; i++)
            {
                _playerPool[i]?.Reset();
            }
            _playerPool.Clear();
            _activePlayers.Clear();

            CleanupPlayableGraph();

            // Restore the Animator's RuntimeAnimatorController.
            if (_animator != null && _originalController != null)
            {
                _animator.runtimeAnimatorController = _originalController;
            }
        }

        #endregion

        #region Public Methods (Play & Stop API)

        /// <summary>
        /// Backward-compatible overload accepting legacy MontageSequenceSO.
        /// </summary>
        public virtual MontageHandle Play(
            MontageSequenceSO montage,
            float? customBlendInTime = null,
            AnimationCurve customBlendInCurve = null,
            bool skipAnimationTracks = false)
        {
            return Play((IMontageSequence)montage, customBlendInTime, customBlendInCurve, skipAnimationTracks);
        }

        /// <summary>
        /// Plays the specified montage sequence configuration (Contract-based: supports pure C# data, new assets, or legacy SO).
        /// Automatically distributes segments across UpperBody, FullBody, and Additive layers with smooth CrossFade transitions.
        /// When skipAnimationTracks is true or sequence has no clips, it runs as a pure standalone logic/presentation player in parallel without interrupting any physical bone layers.
        /// </summary>
        /// <param name="montage">Montage sequence instance (IMontageSequence contract).</param>
        /// <param name="customBlendInTime">Optional custom blend-in duration in seconds.</param>
        /// <param name="customBlendInCurve">Optional custom blend-in curve.</param>
        /// <param name="skipAnimationTracks">Whether to skip mounting bone animation clips and only drive audio, visual and action block tracks in parallel.</param>
        /// <returns>Valid handle for controlling playback, or MontageHandle.Invalid if failed.</returns>
        public virtual MontageHandle Play(
            IMontageSequence montage,
            float? customBlendInTime = null,
            AnimationCurve customBlendInCurve = null,
            bool skipAnimationTracks = false)
        {
            if (montage == null)
            {
                Debug.LogWarning($"[MontageController] Object '{gameObject.name}' cannot play: montage is null.");
                return MontageHandle.Invalid;
            }

            if (!_isGraphInitialized)
            {
                InitializePlayableGraph();
            }

            float blendInDuration = customBlendInTime ?? 0.1f;

            // 1. Get an idle player from the small player pool and reuse it in place (0 GC Alloc).
            var player = GetOrCreatePlayer();
            player.Play(montage, customBlendInTime, customBlendInCurve);
            if (!_activePlayers.Contains(player))
            {
                _activePlayers.Add(player);
            }

            // Register lifecycle callbacks (unbind first as a safeguard against duplicate registration).
            UnbindPlayerEvents(player);
            player.OnSectionEntered += HandleSectionEntered;
            player.OnFinished += HandlePlayerEnded;
            player.OnInterrupted += HandlePlayerEnded;

            bool hasFullBody = !skipAnimationTracks && montage.HasAnyAnimationInChannel(MontageLayerChannel.FullBody);
            bool hasUpperBody = !skipAnimationTracks && montage.HasAnyAnimationInChannel(MontageLayerChannel.UpperBody);
            bool hasAdditive = !skipAnimationTracks && montage.HasAnyAnimationInChannel(MontageLayerChannel.Additive);

            MontageHandle mainHandle = MontageHandle.Invalid;

            // 分支 1：包含有效骨骼动画切片，挂载至对应动画层
            if (hasFullBody || hasUpperBody || hasAdditive)
            {
                if (hasFullBody)
                {
                    float fullBlendIn = ResolveChannelBlendInDuration(montage.FullBodySegments, customBlendInTime);
                    // 全身层独占优先级：播放全身动作时，通知上半身活跃动作按相同的过渡时长平滑淡出
                    StopActiveSlot(_layerStates[0], fullBlendIn);

                    var handle = SetupLayerSlot(_layerStates[1], montage.FullBodySegments, player, fullBlendIn, montage.IsFootIK);
                    if (!mainHandle.IsValid) mainHandle = handle;
                }

                if (hasUpperBody)
                {
                    float upperBlendIn = ResolveChannelBlendInDuration(montage.UpperBodySegments, customBlendInTime);
                    var handle = SetupLayerSlot(_layerStates[0], montage.UpperBodySegments, player, upperBlendIn, montage.IsFootIK);
                    if (!mainHandle.IsValid) mainHandle = handle;
                }

                if (hasAdditive)
                {
                    float addBlendIn = ResolveChannelBlendInDuration(montage.AdditiveSegments, customBlendInTime);
                    var handle = SetupLayerSlot(_layerStates[2], montage.AdditiveSegments, player, addBlendIn, false);
                    if (!mainHandle.IsValid) mainHandle = handle;
                }

                // 首帧姿态与权重对齐
                UpdateLayers(0f);
            }
            else
            {
                // 分支 2：无动画层 / 降级播放 / 纯视听逻辑蒙太奇
                // 彻底不触碰任何身体骨骼图层，不打断任何正在播放的动作，注册为独立逻辑驱动者并行播放！
                int standaloneId = _nextStandaloneId++;
                int generation = 1;
                _standalonePlayers[standaloneId] = (player, generation);
                mainHandle = new MontageHandle(this, -1, standaloneId, generation);
            }

            if (mainHandle.IsValid)
            {
                OnMontageStarted?.Invoke(mainHandle);
            }

            return mainHandle;
        }

        /// <summary>
        /// Gets the active montage playback handle currently running on the specified channel.
        /// </summary>
        /// <param name="channel">Target layer channel.</param>
        public virtual MontageHandle GetActiveHandle(MontageLayerChannel channel)
        {
            int index = (int)channel;
            return GetActiveHandle(index);
        }

        /// <summary>
        /// Gets the active montage playback handle currently running on the specified layer index.
        /// </summary>
        /// <param name="layerIndex">Layer index (0: UpperBody, 1: FullBody, 2: Additive).</param>
        public virtual MontageHandle GetActiveHandle(int layerIndex = 1)
        {
            if (layerIndex < 0 || layerIndex >= _layerStates.Count) return MontageHandle.Invalid;
            var layer = _layerStates[layerIndex];
            if (layer.ActiveSlotIndex < 0) return MontageHandle.Invalid;
            var slot = (layer.ActiveSlotIndex == 0) ? layer.Slot0 : layer.Slot1;
            if (!slot.IsOccupied || slot.Player == null || slot.Player.IsFinished) return MontageHandle.Invalid;
            return new MontageHandle(this, layerIndex, slot.SlotIndex, slot.Generation);
        }

        /// <summary>
        /// Queries whether the specified channel is currently playing a montage.
        /// </summary>
        public virtual bool IsPlayingChannel(MontageLayerChannel channel)
        {
            return IsPlayingLayer((int)channel);
        }

        /// <summary>
        /// Queries whether the specified layer is currently playing a montage.
        /// </summary>
        /// <param name="layerIndex">Layer index.</param>
        public virtual bool IsPlayingLayer(int layerIndex)
        {
            if (layerIndex < 0 || layerIndex >= _layerStates.Count) return false;
            var layer = _layerStates[layerIndex];
            if (layer.ActiveSlotIndex < 0) return false;
            var slot = (layer.ActiveSlotIndex == 0) ? layer.Slot0 : layer.Slot1;
            return slot.IsOccupied && slot.Player != null && !slot.Player.IsFinished && (slot.Player.IsPlaying || slot.Weight > 0.0001f);
        }

        /// <summary>
        /// Stops the montage currently playing on the specified channel.
        /// </summary>
        public virtual void StopChannel(MontageLayerChannel channel, float blendOutTime = 0.1f)
        {
            StopLayer((int)channel, blendOutTime);
        }

        /// <summary>
        /// Stops the montage currently playing on the specified layer.
        /// </summary>
        /// <param name="layerIndex">Layer index.</param>
        /// <param name="blendOutTime">Fade-out duration.</param>
        public virtual void StopLayer(int layerIndex, float blendOutTime = 0.1f)
        {
            if (layerIndex < 0 || layerIndex >= _layerStates.Count)
            {
                Debug.LogWarning($"[MontageController] Layer index {layerIndex} is invalid (valid range: 0 ~ {_layerStates.Count - 1}).");
                return;
            }

            var layer = _layerStates[layerIndex];
            if (layer.Slot0.IsOccupied)
            {
                layer.Slot0.StartFadeOut(blendOutTime);
            }
            if (layer.Slot1.IsOccupied)
            {
                layer.Slot1.StartFadeOut(blendOutTime);
            }
        }

        /// <summary>
        /// Stops the montage currently playing on all layers.
        /// </summary>
        /// <param name="blendOutTime">Fade-out duration.</param>
        public virtual void StopAll(float blendOutTime = 0.1f)
        {
            for (int i = 0; i < _layerStates.Count; i++)
            {
                StopLayer(i, blendOutTime);
            }

            if (_standalonePlayers.Count > 0)
            {
                var list = new List<MontagePlayer>(_standalonePlayers.Count);
                foreach (var kvp in _standalonePlayers)
                {
                    if (kvp.Value.player != null) list.Add(kvp.Value.player);
                }
                for (int i = 0; i < list.Count; i++)
                {
                    list[i].Stop(blendOutTime);
                }
            }
        }

        /// <summary>
        /// Sets the runtime dynamic blend weight multiplier for the specified layer [0.0, 1.0].
        /// </summary>
        /// <param name="layerIndex">Layer index (0: UpperBody, 1: FullBody, 2: Additive).</param>
        /// <param name="weight">Weight value [0.0, 1.0].</param>
        public virtual void SetLayerWeight(int layerIndex, float weight)
        {
            if (layerIndex < 0 || layerIndex >= _layerStates.Count) return;
            _layerStates[layerIndex].LayerWeight = Mathf.Clamp01(weight);
        }

        /// <summary>
        /// Gets the runtime dynamic blend weight multiplier for the specified layer.
        /// </summary>
        /// <param name="layerIndex">Layer index (0: UpperBody, 1: FullBody, 2: Additive).</param>
        public virtual float GetLayerWeight(int layerIndex)
        {
            if (layerIndex < 0 || layerIndex >= _layerStates.Count) return 1.0f;
            return _layerStates[layerIndex].LayerWeight;
        }

        /// <summary>
        /// Sets the runtime dynamic blend weight multiplier for the specified channel [0.0, 1.0].
        /// </summary>
        /// <param name="channel">Target layer channel.</param>
        public virtual void SetChannelWeight(MontageLayerChannel channel, float weight)
        {
            SetLayerWeight((int)channel, weight);
        }

        /// <summary>
        /// Gets the runtime dynamic blend weight multiplier for the specified channel.
        /// </summary>
        /// <param name="channel">Target layer channel.</param>
        public virtual float GetChannelWeight(MontageLayerChannel channel)
        {
            return GetLayerWeight((int)channel);
        }

        /// <summary>
        /// Replaces the underlying Locomotion state machine controller at runtime.
        /// </summary>
        /// <param name="newController">New RuntimeAnimatorController instance.</param>
        public virtual void SetLocomotionAnimatorController(RuntimeAnimatorController newController)
        {
            if (newController == null)
            {
                Debug.LogWarning($"[MontageController] Object '{gameObject.name}' cannot set a null Locomotion Controller.");
                return;
            }

            if (_originalController == newController && _locomotionPlayable.IsValid())
            {
                return;
            }

            _originalController = newController;

            if (!_isGraphInitialized || !_playableGraph.IsValid())
            {
                return;
            }

            // Disconnect and destroy the old Locomotion Playable.
            _topLevelMixer.DisconnectInput(0);
            if (_locomotionPlayable.IsValid())
            {
                _playableGraph.DestroyPlayable(_locomotionPlayable);
            }

            // Create and reconnect the new Locomotion Playable.
            _locomotionPlayable = AnimatorControllerPlayable.Create(_playableGraph, newController);
            _topLevelMixer.ConnectInput(0, _locomotionPlayable, 0);
            _topLevelMixer.SetInputWeight(0, 1.0f);
            CheckSingleLayerControllerWarning();
        }

        /// <summary>
        /// Allows external Playables systems or third-party plugins to inject a custom Playable directly as input 0 of the Locomotion base layer.
        /// </summary>
        /// <param name="customPlayable">Custom Playable node.</param>
        /// <param name="outputPort">Output port index of the external Playable, defaults to 0.</param>
        public virtual void SetLocomotionPlayable(Playable customPlayable, int outputPort = 0)
        {
            if (!customPlayable.IsValid())
            {
                Debug.LogWarning($"[MontageController] Object '{gameObject.name}' received an invalid customPlayable that cannot be connected to the Locomotion slot.");
                return;
            }

            if (!_isGraphInitialized || !_playableGraph.IsValid())
            {
                return;
            }

            if (!customPlayable.GetGraph().Equals(_playableGraph))
            {
                Debug.LogError($"[MontageController] Object '{gameObject.name}' received a customPlayable that belongs to an external PlayableGraph and does not match this coordinator's PlayableGraph. Unity Playables strictly forbids connecting nodes across graphs! Make sure the node is created in this coordinator's graph, or derive and override the graph topology initialization.", this);
                return;
            }

            // Disconnect and destroy the old Locomotion Playable.
            _topLevelMixer.DisconnectInput(0);
            if (_locomotionPlayable.IsValid())
            {
                _playableGraph.DestroyPlayable(_locomotionPlayable);
            }

            _locomotionPlayable = customPlayable;
            _topLevelMixer.ConnectInput(0, _locomotionPlayable, outputPort);
            _topLevelMixer.SetInputWeight(0, 1.0f);
        }

        /// <summary>
        /// Resets the underlying Locomotion to the originally bound Controller.
        /// </summary>
        public virtual void ResetLocomotionAnimatorController()
        {
            if (_originalController != null)
            {
                SetLocomotionAnimatorController(_originalController);
            }
            else
            {
                Debug.LogWarning($"[MontageController] Object '{gameObject.name}' has no recorded initial Controller and cannot be reset.");
            }
        }

        /// <summary>
        /// Gets the target core bone Transform bound to the current character (O(1), zero-GC fast lookup).
        /// </summary>
        /// <param name="targetBone">Core bone enum.</param>
        /// <returns>Target bone Transform; falls back to the character's own transform.</returns>
        public virtual Transform GetTargetBone(MontageTargetBone targetBone)
        {
            int index = (int)targetBone;
            if (index >= 0 && index < _cachedBones.Length)
            {
                var bone = _cachedBones[index];
                if (bone != null) return bone;
            }
            return transform;
        }

        #endregion

        #region Topology & PlayableGraph Initialization

        protected virtual void EnsureAnimator()
        {
            if (_animator != null)
            {
                SetupAnimatorBinding();
                return;
            }

            _animator = GetComponent<Animator>();
            if (_animator != null)
            {
                SetupAnimatorBinding();
                return;
            }

            int childCount = transform.childCount;
            for (int i = 0; i < childCount; i++)
            {
                var childAnimator = transform.GetChild(i).GetComponent<Animator>();
                if (childAnimator != null)
                {
                    _animator = childAnimator;
                    SetupAnimatorBinding();
                    return;
                }
            }

            Debug.LogError($"[MontageController] Object '{gameObject.name}' has no explicitly assigned Animator, and no Animator component was found on itself or its direct children!", this);
        }

        /// <summary>
        /// 快速判定指定的动画剪辑是否与当前 Animator 的骨骼系统兼容匹配。
        /// </summary>
        /// <param name="clip">待检验的动画剪辑</param>
        /// <returns>若骨骼兼容则返回 true；否则返回 false</returns>
        public virtual bool IsClipCompatible(AnimationClip clip)
        {
            if (clip == null) return false;

            if (_animator == null)
            {
                EnsureAnimator();
                if (_animator == null) return false;
            }

            bool isModelHumanoid = _animator.isHuman;
            bool isClipHumanoid = clip.isHumanMotion;

            // 人形模型优先兼容人形重定向动作；非人形泛型骨骼严禁混用人形肌肉动作
            return isModelHumanoid == isClipHumanoid;
        }

        /// <summary>
        /// 兼容旧版 MontageSequenceSO 参数的骨骼校验重载。
        /// </summary>
        public virtual bool IsMontageCompatible(MontageSequenceSO montage)
        {
            return IsMontageCompatible((IMontageSequence)montage);
        }

        /// <summary>
        /// 快速判定指定的蒙太奇序列是否与当前角色的骨骼系统兼容匹配（契约式判定，支持纯 C# 数据模型）。
        /// 1. 若有一方为人形成员：执行 O(1) 疾速判定（严格比对 isHuman 与 isHumanMotion）。
        /// 2. 若双方皆为泛型（Generic）：执行关键特征骨骼节点抽样快速比对，保底防错。
        /// </summary>
        /// <param name="montage">待检验的蒙太奇配置序列（IMontageSequence）</param>
        /// <returns>若骨骼匹配或无需骨骼绑定则返回 true；若骨骼冲突则返回 false</returns>
        public virtual bool IsMontageCompatible(IMontageSequence montage)
        {
            if (montage == null) return false;

            if (_animator == null)
            {
                EnsureAnimator();
                if (_animator == null) return false;
            }

            // 读取蒙太奇原生自带的骨骼类型锚点判定（若无实体动画分段返回 null，视为通用兼容）
            bool? isMontageHuman = montage.GetAnchorSkeletonIsHuman();
            if (!isMontageHuman.HasValue)
            {
                return true;
            }

            bool isModelHuman = _animator.isHuman;
            bool isMontageHumanVal = isMontageHuman.Value;

            // 1. 若有一方为人形：疾速 O(1) 判定
            // - 人形模型必须匹配人形动作（享受统一 Humanoid 重定向）
            // - 泛型怪物严禁播放人形通用动作（肌肉通道不兼容）
            if (isModelHuman || isMontageHumanVal)
            {
                return isModelHuman == isMontageHumanVal;
            }

            // 2. 双方皆为泛型特异骨骼 (Generic)：执行特征骨骼指纹抽样保底校验
            var samplePaths = montage.GenericSampleBonePaths;
            if (samplePaths != null && samplePaths.Count > 0)
            {
                Transform rootTransform = _animator.transform;
                bool anyMatched = false;

                for (int i = 0; i < samplePaths.Count; i++)
                {
                    string path = samplePaths[i];
                    if (string.IsNullOrEmpty(path)) continue;

                    // 快速相对路径探测
                    if (rootTransform.Find(path) != null)
                    {
                        anyMatched = true;
                        break;
                    }

                    // 兼容根节点层级嵌套差异：匹配末级骨骼名
                    int lastSlash = path.LastIndexOf('/');
                    string boneName = lastSlash >= 0 ? path.Substring(lastSlash + 1) : path;
                    if (FindBoneRecursive(rootTransform, boneName, 3) != null)
                    {
                        anyMatched = true;
                        break;
                    }
                }

                // 抽样的特征骨骼若无一匹配，说明骨骼层级完全不吻合，拦截防畸变
                if (!anyMatched)
                {
                    return false;
                }
            }

            // 抽样命中，或无抽样指纹（旧资产保底放行）
            return true;
        }

        /// <summary>
        /// 递归向下查找指定名称的骨骼节点（带深度限制，防止过深遍历）。
        /// </summary>
        protected virtual Transform FindBoneRecursive(Transform current, string boneName, int maxDepth)
        {
            if (current == null || maxDepth < 0) return null;
            if (current.name.Equals(boneName, StringComparison.OrdinalIgnoreCase)) return current;

            int childCount = current.childCount;
            for (int i = 0; i < childCount; i++)
            {
                var found = FindBoneRecursive(current.GetChild(i), boneName, maxDepth - 1);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// 自动检查目标 Animator 的骨骼类型（基于 animator.isHuman 与 avatar.isHuman 进行识别）。
        /// </summary>
        public virtual MontageSkeletonType DetectSkeletonType()
        {
            if (_animator == null)
            {
                EnsureAnimator();
            }

            if (_animator != null)
            {
                if (_animator.avatar != null && _animator.avatar.isValid)
                {
                    return _animator.avatar.isHuman ? MontageSkeletonType.Humanoid : MontageSkeletonType.Generic;
                }
                return _animator.isHuman ? MontageSkeletonType.Humanoid : MontageSkeletonType.Generic;
            }

            return MontageSkeletonType.Generic;
        }

        protected virtual void SetupAnimatorBinding()
        {
            if (_animator == null) return;

            _originalController = _animator.runtimeAnimatorController;
            _skeletonType = DetectSkeletonType();
            MontageBoneUtility.ResolveBones(gameObject, _animator, _cachedBones);

            SetupAnimatorDispatcher();
        }

        /// <summary>
        /// Creates and binds the Animator's root motion sampling dispatcher component.
        /// Subclasses can override this method to customize or disable OnAnimatorMove interception (for example, when the external host already has a root motion pipeline).
        /// </summary>
        protected virtual void SetupAnimatorDispatcher()
        {
            _animatorDispatcher = _animator.GetComponent<MontageAnimatorDispatcher>();
            if (_animatorDispatcher == null)
            {
                _animatorDispatcher = _animator.gameObject.AddComponent<MontageAnimatorDispatcher>();
            }
            _animatorDispatcher.Bind(HandleAnimatorMove);
        }

        /// <summary>
        /// Creates and initializes the PlayableGraph instance. Subclasses can override this method to reuse an external host graph (such as Animancer).
        /// </summary>
        protected virtual PlayableGraph CreatePlayableGraph()
        {
            var graph = PlayableGraph.Create($"CwcMontage_{gameObject.name}");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            return graph;
        }

        /// <summary>
        /// Creates the animation output node bound to the target Animator. If a subclass mounts this coordinator as a layer in an external mixer, it can override this method to return default so the Animator output channel is not taken over.
        /// </summary>
        protected virtual PlayableOutput CreatePlayableOutput(PlayableGraph graph)
        {
            return AnimationPlayableOutput.Create(graph, "MontageOutput", _animator);
        }

        /// <summary>
        /// Creates and configures the underlying Locomotion base layer node (Input 0).
        /// Subclasses can override this method to connect the output node of an external state machine or third-party animation framework (such as Animancer).
        /// </summary>
        /// <param name="graph">Current PlayableGraph instance.</param>
        /// <returns>The built Locomotion root Playable node; returns Playable.Null if there is no base layer.</returns>
        protected virtual Playable CreateLocomotionPlayable(PlayableGraph graph)
        {
            if (_originalController != null)
            {
                var playable = AnimatorControllerPlayable.Create(graph, _originalController);
                CheckSingleLayerControllerWarning();
                return playable;
            }

            Debug.LogWarning($"[MontageController] The Animator of object '{gameObject.name}' has no RuntimeAnimatorController assigned; the Locomotion base layer input is empty.");
            return Playable.Null;
        }

        /// <summary>
        /// Gets the AvatarMask bone mask used by the UpperBody layer.
        /// Subclasses can override this method to customize the upper-body mask.
        /// </summary>
        protected virtual AvatarMask GetUpperBodyMask()
        {
            return _customUpperBodyMask != null ? _customUpperBodyMask : MontageMaskUtility.GetOrCreateHumanoidUpperBodyMask();
        }

        protected virtual void InitializePlayableGraph()
        {
            if (_animator == null)
            {
                Debug.LogError($"[MontageController] Object '{gameObject.name}' is missing an Animator component; PlayableGraph initialization failed.");
                return;
            }

            _playableGraph = CreatePlayableGraph();
            var playableOutput = CreatePlayableOutput(_playableGraph);

            // Fixed four-layer topology:
            // Input 0: Locomotion (base state machine)
            // Input 1: UpperBody (upper body blended with movement, built-in Humanoid mask)
            // Input 2: FullBody (exclusive full-body actions, dominant override)
            // Input 3: Additive (hit/shake additive overlay, Additive = true)
            _topLevelMixer = AnimationLayerMixerPlayable.Create(_playableGraph, 4);

            _locomotionPlayable = CreateLocomotionPlayable(_playableGraph);
            if (_locomotionPlayable.IsValid())
            {
                _topLevelMixer.ConnectInput(0, _locomotionPlayable, 0);
                _topLevelMixer.SetInputWeight(0, 1.0f);
            }
            else
            {
                _topLevelMixer.SetInputWeight(0, 0.0f);
            }

            _layerStates.Clear();

            // 1. UpperBody (Layer 0, Top Input 1)
            var upperLayer = CreateLayerState(MontageLayerChannel.UpperBody, 0, 1);
            var upperMask = GetUpperBodyMask();
            _topLevelMixer.SetLayerMaskFromAvatarMask(1, upperMask);
            _topLevelMixer.SetLayerAdditive(1, false);
            _layerStates.Add(upperLayer);

            // 2. FullBody (Layer 1, Top Input 2)
            var fullLayer = CreateLayerState(MontageLayerChannel.FullBody, 1, 2);
            _topLevelMixer.SetLayerAdditive(2, false);
            _layerStates.Add(fullLayer);

            // 3. Additive (Layer 2, Top Input 3)
            var addLayer = CreateLayerState(MontageLayerChannel.Additive, 2, 3);
            _topLevelMixer.SetLayerAdditive(3, true);
            _layerStates.Add(addLayer);

            if (playableOutput.IsOutputValid())
            {
                _animator.runtimeAnimatorController = null;
                _animator.applyRootMotion = true;
                playableOutput.SetSourcePlayable(_topLevelMixer);
            }
            _playableGraph.Play();
            _isGraphInitialized = true;
        }

        protected virtual LayerRuntimeState CreateLayerState(MontageLayerChannel channel, int layerIndex, int topInputIndex)
        {
            var state = new LayerRuntimeState(channel, layerIndex, topInputIndex);
            var layerMixer = AnimationMixerPlayable.Create(_playableGraph, 2);
            state.LayerMixer = layerMixer;

            _topLevelMixer.ConnectInput(topInputIndex, layerMixer, 0);
            _topLevelMixer.SetInputWeight(topInputIndex, 0.0f);
            return state;
        }

        protected virtual void CleanupPlayableGraph()
        {
            DestroyInternalPlayables();
            DestroyPlayableGraph();
            _isGraphInitialized = false;
        }

        /// <summary>
        /// Disconnects and destroys all internal child Playable nodes created in the PlayableGraph by this coordinator (mixers, slots, Locomotion, and so on).
        /// Ensures no node memory or port leaks occur, whether this coordinator destroys the whole graph or an external shared graph (such as Animancer) is reused.
        /// </summary>
        protected virtual void DestroyInternalPlayables()
        {
            if (!_playableGraph.IsValid())
            {
                return;
            }

            for (int i = 0; i < _layerStates.Count; i++)
            {
                var layer = _layerStates[i];
                layer.Slot0.DestroyPlayables(_playableGraph, layer.LayerMixer);
                layer.Slot1.DestroyPlayables(_playableGraph, layer.LayerMixer);

                if (layer.LayerMixer.IsValid())
                {
                    if (_topLevelMixer.IsValid())
                    {
                        _topLevelMixer.DisconnectInput(layer.TopLevelInputIndex);
                    }
                    _playableGraph.DestroyPlayable(layer.LayerMixer);
                    layer.LayerMixer = default;
                }
            }
            _layerStates.Clear();

            foreach (var kvp in _standalonePlayers)
            {
                var p = kvp.Value.player;
                if (p != null)
                {
                    UnbindPlayerEvents(p);
                    p.Reset();
                }
            }
            _standalonePlayers.Clear();

            if (_topLevelMixer.IsValid())
            {
                _topLevelMixer.DisconnectInput(0);
            }

            if (_locomotionPlayable.IsValid())
            {
                _playableGraph.DestroyPlayable(_locomotionPlayable);
                _locomotionPlayable = default;
            }

            if (_topLevelMixer.IsValid())
            {
                _playableGraph.DestroyPlayable(_topLevelMixer);
                _topLevelMixer = default;
            }
        }

        /// <summary>
        /// Destroys the PlayableGraph instance used at runtime.
        /// If a subclass reuses an external host graph (such as Animancer), it can override this method to only clear references or unregister listeners without destroying the host graph.
        /// </summary>
        protected virtual void DestroyPlayableGraph()
        {
            if (_playableGraph.IsValid())
            {
                _playableGraph.Destroy();
                _playableGraph = default;
            }
        }

        protected virtual void CheckSingleLayerControllerWarning()
        {
            if (_animator != null && _animator.isHuman && _locomotionPlayable.IsValid() && _locomotionPlayable.IsPlayableOfType<AnimatorControllerPlayable>())
            {
                var acp = (AnimatorControllerPlayable)_locomotionPlayable;
                if (acp.GetLayerCount() <= 1)
                {
                    string controllerName = _originalController != null ? _originalController.name : "Unknown";
                    Debug.LogWarning(
                        $"[MontageController] The animator controller '{controllerName}' bound to character '{gameObject.name}' contains only 1 layer (Base Layer).\n" +
                        "[Known Unity native defect warning]: When the native Unity Humanoid solver is combined with a Playables layer mixer, a base controller with only a single layer automatically triggers single-layer Mass Center Retargeting, which causes abnormal twisting and deflection of the base character's Hips and body facing while an upper-body montage is playing.\n" +
                        "[Recommended fix]: Add an empty layer to '{controllerName}' (for example, name it 'EmptyLayer' and set its weight to 0). When the controller has 2 or more layers, Unity automatically switches to the standard multi-layer pipeline and eliminates this deflection entirely.",
                        this);
                }
            }
        }

        #endregion

        #region Slot Configuration & Channel Dispatch

        protected static float ResolveChannelBlendInDuration(
            IReadOnlyList<MontageAnimationSegment> segments,
            float? customBlendInTime)
        {
            if (customBlendInTime.HasValue)
            {
                return Mathf.Max(0f, customBlendInTime.Value);
            }

            if (segments != null && segments.Count > 0 && segments[0] != null && segments[0].StartTime <= 0.0001f)
            {
                return Mathf.Max(0f, segments[0].BlendInTime);
            }

            return 0.1f;
        }

        protected virtual void StopActiveSlot(LayerRuntimeState layer, float blendOutDuration)
        {
            if (layer.ActiveSlotIndex < 0) return;
            var slot = (layer.ActiveSlotIndex == 0) ? layer.Slot0 : layer.Slot1;
            if (slot.IsOccupied)
            {
                slot.StartFadeOut(blendOutDuration);
            }
        }

        protected virtual void EvictSlot(LayerRuntimeState layerState, SlotState slot)
        {
            if (!slot.IsOccupied) return;

            var evictedPlayer = slot.Player;
            slot.DestroyPlayables(_playableGraph, layerState.LayerMixer);
            slot.Reset();

            if (evictedPlayer != null && !IsPlayerOccupiedByAnySlot(evictedPlayer))
            {
                if (!evictedPlayer.IsFinished)
                {
                    evictedPlayer.Terminate();
                }
                UnbindPlayerEvents(evictedPlayer);
                ReleasePlayer(evictedPlayer);
            }
        }

        protected virtual MontageHandle SetupLayerSlot(
            LayerRuntimeState layerState,
            IReadOnlyList<MontageAnimationSegment> segments,
            MontagePlayer player,
            float blendInDuration,
            bool isFootIK)
        {
            int newSlotIndex = (layerState.ActiveSlotIndex == 0) ? 1 : 0;
            int oldSlotIndex = (newSlotIndex == 0) ? 1 : 0;

            var newSlot = (newSlotIndex == 0) ? layerState.Slot0 : layerState.Slot1;
            var oldSlot = (oldSlotIndex == 0) ? layerState.Slot0 : layerState.Slot1;

            if (oldSlot.IsOccupied)
            {
                oldSlot.StartFadeOut(blendInDuration);
            }

            if (newSlot.IsOccupied)
            {
                EvictSlot(layerState, newSlot);
            }

            newSlot.DestroyPlayables(_playableGraph, layerState.LayerMixer);

            int segCount = segments != null ? segments.Count : 0;
            var slotMixer = AnimationMixerPlayable.Create(_playableGraph, Mathf.Max(1, segCount));
            newSlot.SlotMixer = slotMixer;
            newSlot.SegmentPlayables.Clear();

            for (int s = 0; s < segCount; s++)
            {
                var seg = segments[s];
                var clipToUse = seg?.Clip;
                var cp = clipToUse != null
                    ? AnimationClipPlayable.Create(_playableGraph, clipToUse)
                    : default;

                if (cp.IsValid())
                {
                    cp.SetApplyFootIK(isFootIK);
                    cp.SetSpeed(1.0f);
                    slotMixer.ConnectInput(s, cp, 0);
                }

                slotMixer.SetInputWeight(s, s == 0 ? 1.0f : 0.0f);
                newSlot.SegmentPlayables.Add(cp);
            }

            layerState.LayerMixer.ConnectInput(newSlot.SlotIndex, slotMixer, 0);
            layerState.LayerMixer.SetInputWeight(newSlot.SlotIndex, 0.0f);

            newSlot.Bind(player, blendInDuration);
            layerState.ActiveSlotIndex = newSlotIndex;

            return new MontageHandle(this, layerState.LayerIndex, newSlotIndex, newSlot.Generation);
        }

        #endregion

        #region Graph Evaluation & Layer Mixing

        protected virtual void UpdateLayers(float deltaTime)
        {
            // 1. Gather all active players and advance their single authoritative clock and action block sweep once (so a clock shared by multiple channels advances only once).
            _tickablePlayers.Clear();
            for (int i = 0; i < _layerStates.Count; i++)
            {
                var layer = _layerStates[i];
                if (layer.Slot0.IsOccupied && layer.Slot0.Player != null) _tickablePlayers.Add(layer.Slot0.Player);
                if (layer.Slot1.IsOccupied && layer.Slot1.Player != null) _tickablePlayers.Add(layer.Slot1.Player);
            }

            foreach (var kvp in _standalonePlayers)
            {
                if (kvp.Value.player != null)
                {
                    _tickablePlayers.Add(kvp.Value.player);
                }
            }

            foreach (var p in _tickablePlayers)
            {
                p.Tick(deltaTime);
                p.SetCurrentWeight(0f);
            }

            // 2. 推进各图层槽位的骨骼姿态采样与正交权重归一化混合
            for (int i = 0; i < _layerStates.Count; i++)
            {
                var layer = _layerStates[i];
                var mixer = layer.LayerMixer;

                SlotState activeSlot = (layer.ActiveSlotIndex == 0) ? layer.Slot0 : layer.Slot1;
                SlotState fadingSlot = (layer.ActiveSlotIndex == 0) ? layer.Slot1 : layer.Slot0;

                activeSlot.TickTransition(deltaTime);
                fadingSlot.TickTransition(deltaTime);

                UpdateSlotClips(layer, activeSlot, isFadingOut: activeSlot.TransitionState == SlotTransitionState.FadingOut);
                UpdateSlotClips(layer, fadingSlot, isFadingOut: true);

                // [正交权重与跨槽位连续能量守恒公理]:
                // 1. 宏观槽位过渡权重 (slot.Weight) 与微观切片包络乘数 (slot.CurrentSegmentWeightMultiplier) 正交解耦相乘
                // 2. 新旧槽位对称演进：新槽位按 alpha 爬升，旧槽位从锁存有效权重按 (1 - alpha) 衰减并保持姿态定格
                // 3. combinedWeight = effectiveActiveWeight + effectiveFadingWeight 保证跨蒙太奇与跨状态无凹陷平滑交接
                float activeRawWeight = (activeSlot.IsOccupied && activeSlot.HasActiveAnimation && activeSlot.Player != null)
                    ? activeSlot.Weight * activeSlot.CurrentSegmentWeightMultiplier * activeSlot.Player.CustomWeight
                    : 0f;

                float fadingRawWeight = (fadingSlot.IsOccupied && fadingSlot.HasActiveAnimation && fadingSlot.Player != null)
                    ? fadingSlot.Weight * fadingSlot.CurrentSegmentWeightMultiplier * fadingSlot.Player.CustomWeight
                    : 0f;

                if (activeSlot.IsOccupied && activeSlot.Player != null)
                {
                    float maxW = Mathf.Max(activeSlot.Player.CurrentWeight, activeRawWeight);
                    activeSlot.Player.SetCurrentWeight(maxW);
                }
                if (fadingSlot.IsOccupied && fadingSlot.Player != null)
                {
                    float maxW = Mathf.Max(fadingSlot.Player.CurrentWeight, fadingRawWeight);
                    fadingSlot.Player.SetCurrentWeight(maxW);
                }

                float activeAssetWeight = (activeSlot.IsOccupied && activeSlot.Player?.Sequence != null)
                    ? activeSlot.Player.Sequence.GetChannelWeight(layer.Channel)
                    : 1.0f;
                float fadingAssetWeight = (fadingSlot.IsOccupied && fadingSlot.Player?.Sequence != null)
                    ? fadingSlot.Player.Sequence.GetChannelWeight(layer.Channel)
                    : 1.0f;

                float effectiveActiveWeight = activeRawWeight * activeAssetWeight;
                float effectiveFadingWeight = fadingRawWeight * fadingAssetWeight;

                float combinedWeight = effectiveActiveWeight + effectiveFadingWeight;
                if (combinedWeight > 0.0001f)
                {
                    mixer.SetInputWeight(activeSlot.SlotIndex, effectiveActiveWeight / combinedWeight);
                    mixer.SetInputWeight(fadingSlot.SlotIndex, effectiveFadingWeight / combinedWeight);
                }
                else
                {
                    mixer.SetInputWeight(activeSlot.SlotIndex, 0f);
                    mixer.SetInputWeight(fadingSlot.SlotIndex, 0f);
                }

                // Update the TopLevelMixer input weight (combines the montage asset's layer weight with the runtime dynamic layer weight multiplier).
                float finalTopLayerWeight = combinedWeight * layer.LayerWeight;
                _topLevelMixer.SetInputWeight(layer.TopLevelInputIndex, Mathf.Clamp01(finalTopLayerWeight));

                // Release slots that have fully finished and whose weight has reached zero.
                CheckAndReleaseSlot(layer, layer.Slot0, mixer);
                CheckAndReleaseSlot(layer, layer.Slot1, mixer);
            }
        }

        protected virtual void UpdateSlotClips(LayerRuntimeState layer, SlotState slot, bool isFadingOut)
        {
            if (!slot.IsOccupied)
            {
                slot.Weight = 0f;
                slot.HasActiveAnimation = false;
                slot.CurrentSegmentWeightMultiplier = 0f;
                return;
            }

            var player = slot.Player;
            bool freezeForFadeOut = isFadingOut || slot.TransitionState == SlotTransitionState.FadingOut;

            if (slot.SlotMixer.IsValid() && slot.SegmentPlayables.Count > 0)
            {
                player.EvaluateChannelSegments(layer.Channel, slot.TempEvalIndices, slot.TempEvalTimes, slot.TempEvalWeights);

                if (slot.TempEvalIndices.Count > 0)
                {
                    slot.HasActiveAnimation = true;
                    float totalSegWeight = 0f;
                    for (int w = 0; w < slot.TempEvalWeights.Count; w++)
                    {
                        totalSegWeight += slot.TempEvalWeights[w];
                    }

                    if (freezeForFadeOut)
                    {
                        // 淡出阶段已在 StartFadeOut 中锁存了切出瞬间的真实有效权重 (Weight * CurrentSegmentWeightMultiplier)，此处保持 1.0 避免二次衰减
                        slot.CurrentSegmentWeightMultiplier = 1.0f;
                    }
                    else
                    {
                        // 若当前仅首段切片活跃且该切片从 0s 开始、尚未进入末尾自然淡出区，其切入包络已由宏观槽位 FadingIn 接管，内部乘数恒为 1.0f 以消除二次平方淡入
                        var segs = GetChannelSegments(layer.Channel, player?.Sequence);
                        bool isInitialSegmentActive = slot.TempEvalIndices.Count == 1
                            && slot.TempEvalIndices[0] == 0
                            && segs != null && segs.Count > 0 && segs[0] != null
                            && segs[0].StartTime <= 0.0001f
                            && player.ElapsedTime < segs[0].EndTime;
                        slot.CurrentSegmentWeightMultiplier = isInitialSegmentActive ? 1.0f : Mathf.Clamp01(totalSegWeight);
                    }

                    int count = slot.SegmentPlayables.Count;
                    for (int i = 0; i < count; i++)
                    {
                        slot.SlotMixer.SetInputWeight(i, 0.0f);
                    }

                    // Feed normalized segment weights into the SlotMixer so its internal sum is always exactly 1.0f!
                    for (int k = 0; k < slot.TempEvalIndices.Count; k++)
                    {
                        int segIdx = slot.TempEvalIndices[k];
                        if (segIdx >= 0 && segIdx < count)
                        {
                            var cp = slot.SegmentPlayables[segIdx];
                            if (cp.IsValid())
                            {
                                cp.SetTime(slot.TempEvalTimes[k]);
                                cp.SetSpeed(freezeForFadeOut ? 0.0f : 1.0f);
                            }
                            float normalizedW = totalSegWeight > 0.0001f
                                ? (slot.TempEvalWeights[k] / totalSegWeight)
                                : (k == slot.TempEvalIndices.Count - 1 ? 1.0f : 0.0f);
                            slot.SlotMixer.SetInputWeight(segIdx, normalizedW);
                        }
                    }
                }
                else if (freezeForFadeOut && (slot.IsHoldingPoseOnFinish || player.IsFinished || player.ElapsedTime >= player.TotalDuration))
                {
                    // 跨 Slot 交接保护：旧 Slot 在淡出退出期间到达或超过时间轴末尾时，执行末帧定格（Pose Hold）
                    // 将该通道上的最后一个有效切片锁死在 EndOffset，并提供 1.0 满额基底姿势供平滑让渡
                    int count = slot.SegmentPlayables.Count;
                    int lastIdx = count - 1;
                    for (int i = 0; i < count; i++)
                    {
                        slot.SlotMixer.SetInputWeight(i, i == lastIdx ? 1.0f : 0.0f);
                    }

                    var lastPlayable = slot.SegmentPlayables[lastIdx];
                    if (lastPlayable.IsValid())
                    {
                        float holdTime = 0f;
                        var segs = GetChannelSegments(layer.Channel, player?.Sequence);
                        if (segs != null && lastIdx < segs.Count && segs[lastIdx] != null)
                        {
                            holdTime = segs[lastIdx].EndOffset;
                        }
                        else if (lastPlayable.GetAnimationClip() != null)
                        {
                            holdTime = lastPlayable.GetAnimationClip().length;
                        }

                        lastPlayable.SetTime(holdTime);
                        lastPlayable.SetSpeed(0.0f);
                    }

                    slot.HasActiveAnimation = true;
                    slot.CurrentSegmentWeightMultiplier = 1.0f;
                }
                else
                {
                    slot.HasActiveAnimation = false;
                    slot.CurrentSegmentWeightMultiplier = 0f;
                }
            }
            else
            {
                slot.HasActiveAnimation = false;
                slot.CurrentSegmentWeightMultiplier = 0f;
            }
        }

        protected static IReadOnlyList<MontageAnimationSegment> GetChannelSegments(MontageLayerChannel channel, IMontageSequence seq)
        {
            if (seq == null) return null;
            return channel switch
            {
                MontageLayerChannel.UpperBody => seq.UpperBodySegments,
                MontageLayerChannel.FullBody => seq.FullBodySegments,
                MontageLayerChannel.Additive => seq.AdditiveSegments,
                _ => null
            };
        }

        protected virtual void CheckAndReleaseSlot(LayerRuntimeState layer, SlotState slot, AnimationMixerPlayable mixer)
        {
            if (!slot.IsOccupied) return;

            var player = slot.Player;

            // 活跃主槽位安全保护：只要当前主槽位处于正常播放期（未进入逻辑完成态，且未进入 FadingOut / Inactive），严禁释放
            bool isActiveSlot = layer != null && layer.ActiveSlotIndex == slot.SlotIndex;
            bool isNormalPlaying = player != null
                && player.State == MontagePlayerState.Playing
                && !player.IsLogicalFinished
                && slot.TransitionState != SlotTransitionState.FadingOut
                && slot.TransitionState != SlotTransitionState.Inactive;
            if (isActiveSlot && isNormalPlaying)
            {
                return;
            }

            bool isFadeOutCompleted = slot.TransitionState == SlotTransitionState.Inactive;
            bool isNaturalTailCompleted = player == null
                || player.State == MontagePlayerState.Finished
                || (player.IsLogicalFinished && (!slot.HasActiveAnimation || slot.CurrentSegmentWeightMultiplier <= 0.0001f));

            if (isFadeOutCompleted || isNaturalTailCompleted)
            {
                slot.DestroyPlayables(_playableGraph, mixer);
                slot.Reset();

                if (player != null && !IsPlayerOccupiedByAnySlot(player))
                {
                    UnbindPlayerEvents(player);
                    ReleasePlayer(player);
                }
            }
        }

        protected virtual bool IsPlayerOccupiedByAnySlot(MontagePlayer player)
        {
            for (int i = 0; i < _layerStates.Count; i++)
            {
                var layer = _layerStates[i];
                if (layer.Slot0.Player == player || layer.Slot1.Player == player)
                {
                    return true;
                }
            }
            return false;
        }

        protected virtual MontagePlayer GetOrCreatePlayer()
        {
            for (int i = 0; i < _playerPool.Count; i++)
            {
                var candidate = _playerPool[i];
                if (!_activePlayers.Contains(candidate))
                {
                    return candidate;
                }
            }

            var newPlayer = CreatePlayerInstance();
            _playerPool.Add(newPlayer);
            return newPlayer;
        }

        /// <summary>
        /// Constructs a new montage player instance.
        /// Subclasses can override this method to instantiate a custom derived MontagePlayer.
        /// </summary>
        protected virtual MontagePlayer CreatePlayerInstance()
        {
            return new MontagePlayer(gameObject, _animator, isPreview: false, controller: this);
        }

        protected virtual void ReleasePlayer(MontagePlayer player)
        {
            if (player == null) return;
            _activePlayers.Remove(player);
            player.Reset();
        }

        protected virtual void ApplyUpperBodySpineDecoupling()
        {
            // Spine decoupling orientation correction is strictly limited to the generic procedural standard upper-body mask; if a custom mask is configured, the custom mask has full authority and is never overridden.
            if (_animator == null || !_animator.isHuman || _customUpperBodyMask != null || _layerStates.Count <= 0) return;

            // 0. If a FullBody action is playing with a valid weight, keep the full-body pose and skip the local Spine orientation correction.
            if (_layerStates.Count > 1)
            {
                var fullLayer = _layerStates[1];
                if (fullLayer.ActiveSlotIndex >= 0)
                {
                    var fullSlot = (fullLayer.ActiveSlotIndex == 0) ? fullLayer.Slot0 : fullLayer.Slot1;
                    if (fullSlot.IsOccupied && fullSlot.HasActiveAnimation && fullSlot.Weight > 0.0001f)
                    {
                        return;
                    }
                }
            }

            var upperLayer = _layerStates[0];
            if (upperLayer.ActiveSlotIndex < 0) return;

            SlotState activeSlot = (upperLayer.ActiveSlotIndex == 0) ? upperLayer.Slot0 : upperLayer.Slot1;
            if (!activeSlot.IsOccupied || activeSlot.Player == null || activeSlot.Player.IsFinished) return;
            if (!activeSlot.HasActiveAnimation || activeSlot.Weight <= 0.0001f) return;

            var montage = activeSlot.Player.Sequence;
            if (montage == null || !montage.DecoupleUpperBodyOrientation) return;

            var upperSegments = montage.UpperBodySegments;
            if (upperSegments == null || upperSegments.Count == 0 || activeSlot.TempEvalIndices.Count == 0) return;

            // Strictly use the Animator's Transform as the root reference frame so the sampling basis stays in exactly the same local coordinate space as the dummy.
            Transform root = _animator != null ? _animator.transform : transform;
            Transform hips = _cachedBones[(int)MontageTargetBone.Hips];
            Transform spine = _cachedBones[(int)MontageTargetBone.Spine];
            if (root == null || hips == null || spine == null) return;

            // 1. Dynamically get the source animation's combined orientation Q_Target of the reference bone relative to the root at the current time.
            Quaternion targetSpineInRoot = Quaternion.identity;
            float totalWeight = 0f;

            for (int k = 0; k < activeSlot.TempEvalIndices.Count; k++)
            {
                int segIdx = activeSlot.TempEvalIndices[k];
                if (segIdx >= 0 && segIdx < upperSegments.Count)
                {
                    var seg = upperSegments[segIdx];
                    if (seg?.Clip != null)
                    {
                        var track = MontageSpineDecoupleUtility.GetOrCreateTrack(seg.Clip, _animator);
                        if (track != null)
                        {
                            Quaternion sample = track.Evaluate(activeSlot.TempEvalTimes[k]);
                            float w = activeSlot.TempEvalWeights[k];
                            if (totalWeight <= 0.0001f)
                            {
                                targetSpineInRoot = sample;
                                totalWeight = w;
                            }
                            else
                            {
                                float blendT = w / (totalWeight + w);
                                targetSpineInRoot = Quaternion.Slerp(targetSpineInRoot, sample, blendT);
                                totalWeight += w;
                            }
                        }
                    }
                }
            }

            if (totalWeight <= 0.0001f) return;

            // 2. Core decoupling solve: compute the base local rotation of the Spine under the current Hips so the Spine's world orientation is locked exactly to targetSpineInRoot in the character's root space.
            Quaternion targetSpineWorld = root.rotation * targetSpineInRoot;
            Quaternion decoupledLocal = Quaternion.Inverse(hips.rotation) * targetSpineWorld;

            // 3. Composite additive pose delta: keeps the decoupling limited to UpperBody and smoothly layers the additive hit/fire shake on top of the decoupled pose to prevent spine breakage.
            Quaternion finalDecoupledLocal = decoupledLocal;
            if (_layerStates.Count > 2)
            {
                var addLayer = _layerStates[2];
                float addTopWeight = _topLevelMixer.GetInputWeight(addLayer.TopLevelInputIndex);
                if (addTopWeight > 0.0001f && addLayer.ActiveSlotIndex >= 0)
                {
                    var addSlot = (addLayer.ActiveSlotIndex == 0) ? addLayer.Slot0 : addLayer.Slot1;
                    if (addSlot.IsOccupied && addSlot.HasActiveAnimation && addSlot.TempEvalIndices.Count > 0 && addSlot.Player?.Sequence != null)
                    {
                        var addSegments = addSlot.Player.Sequence.AdditiveSegments;
                        Quaternion additiveSpineDelta = Quaternion.identity;
                        float addTotalWeight = 0f;

                        for (int a = 0; a < addSlot.TempEvalIndices.Count; a++)
                        {
                            int addSegIdx = addSlot.TempEvalIndices[a];
                            if (addSegIdx >= 0 && addSegIdx < addSegments.Count)
                            {
                                var seg = addSegments[addSegIdx];
                                if (seg?.Clip != null)
                                {
                                    var addTrack = MontageSpineDecoupleUtility.GetOrCreateAdditiveTrack(seg.Clip, _animator);
                                    if (addTrack != null)
                                    {
                                        Quaternion sample = addTrack.Evaluate(addSlot.TempEvalTimes[a]);
                                        float w = addSlot.TempEvalWeights[a];
                                        if (addTotalWeight <= 0.0001f)
                                        {
                                            additiveSpineDelta = sample;
                                            addTotalWeight = w;
                                        }
                                        else
                                        {
                                            float blendT = w / (addTotalWeight + w);
                                            additiveSpineDelta = Quaternion.Slerp(additiveSpineDelta, sample, blendT);
                                            addTotalWeight += w;
                                        }
                                    }
                                }
                            }
                        }

                        if (addTotalWeight > 0.0001f)
                        {
                            float effectiveAddWeight = Mathf.Clamp01(addTopWeight * addTotalWeight);
                            Quaternion appliedDelta = Quaternion.Slerp(Quaternion.identity, additiveSpineDelta, effectiveAddWeight);
                            finalDecoupledLocal = decoupledLocal * appliedDelta;
                        }
                    }
                }
            }

            // 4. Smoothly interpolate using the UpperBody layer's actual blend weight (handles fade-in, fade-out, and empty-gap transitions with no twitching).
            float blendWeight = Mathf.Clamp01(activeSlot.Weight * montage.UpperBodyWeight * upperLayer.LayerWeight);
            spine.localRotation = Quaternion.Slerp(spine.localRotation, finalDecoupledLocal, blendWeight);
        }

        #endregion

        #region Root Motion Sampling & Delegation

        protected virtual void HandleAnimatorMove(Vector3 deltaPosition, Quaternion deltaRotation)
        {
            // FullBody (Layer 1) has the highest authority over Root Motion.
            var fullBodyLayer = _layerStates.Count > 1 ? _layerStates[1] : null;
            if (fullBodyLayer == null) return;

            SlotState activeSlot = (fullBodyLayer.ActiveSlotIndex == 0) ? fullBodyLayer.Slot0 : fullBodyLayer.Slot1;
            SlotState fadingSlot = (fullBodyLayer.ActiveSlotIndex == 0) ? fullBodyLayer.Slot1 : fullBodyLayer.Slot0;

            MontagePlayer dominantPlayer = null;
            if (activeSlot.IsOccupied && activeSlot.Player.IsPlaying && activeSlot.Weight > 0.0001f)
            {
                dominantPlayer = activeSlot.Player;
            }
            else if (fadingSlot.IsOccupied && fadingSlot.Weight > 0.0001f)
            {
                dominantPlayer = fadingSlot.Player;
            }
            else if (activeSlot.IsOccupied && activeSlot.Weight > 0.0001f)
            {
                dominantPlayer = activeSlot.Player;
            }

            Vector3 appliedDeltaPos = Vector3.zero;
            Quaternion appliedDeltaRot = Quaternion.identity;

            if (dominantPlayer != null && dominantPlayer.Sequence != null)
            {
                var seq = dominantPlayer.Sequence;

                if (seq.ApplyHorizontalRootMotion)
                {
                    appliedDeltaPos.x = deltaPosition.x;
                    appliedDeltaPos.z = deltaPosition.z;
                }

                if (seq.ApplyVerticalRootMotion)
                {
                    appliedDeltaPos.y = deltaPosition.y;
                }

                appliedDeltaRot = seq.ApplyRotationRootMotion ? deltaRotation : Quaternion.identity;
            }
            else
            {
                // With no active full-body montage, pass through the native Root Motion of the underlying Locomotion directly.
                appliedDeltaPos = deltaPosition;
                appliedDeltaRot = deltaRotation;
            }

            if (_cachedReceiver != null)
            {
                if (appliedDeltaPos != Vector3.zero)
                {
                    _cachedReceiver.OnMontageRootMotionDisplacement(appliedDeltaPos);
                }

                if (appliedDeltaRot != Quaternion.identity)
                {
                    _cachedReceiver.OnMontageRootMotionRotation(appliedDeltaRot);
                }
            }

            if (appliedDeltaPos != Vector3.zero || appliedDeltaRot != Quaternion.identity)
            {
                OnRootMotionDelta?.Invoke(appliedDeltaPos, appliedDeltaRot);
            }
        }

        #endregion

        #region Internal Event Handlers

        protected virtual void HandleSectionEntered(MontagePlayer player, int sectionIndex)
        {
            if (TryGetHandleForPlayer(player, out var handle))
            {
                OnSectionChanged?.Invoke(handle, sectionIndex);
            }
        }

        protected virtual void HandlePlayerEnded(MontagePlayer player)
        {
            if (TryGetHandleForPlayer(player, out var handle))
            {
                OnMontageEnded?.Invoke(handle);
            }

            UnbindPlayerEvents(player);

            // 清理独立纯逻辑/视听播放器
            int foundId = -1;
            foreach (var kvp in _standalonePlayers)
            {
                if (kvp.Value.player == player)
                {
                    foundId = kvp.Key;
                    break;
                }
            }
            if (foundId != -1)
            {
                _standalonePlayers.Remove(foundId);
                _activePlayers.Remove(player);
                ReleasePlayer(player);
            }
        }

        protected virtual void UnbindPlayerEvents(MontagePlayer player)
        {
            if (player == null) return;

            player.OnSectionEntered -= HandleSectionEntered;
            player.OnFinished -= HandlePlayerEnded;
            player.OnInterrupted -= HandlePlayerEnded;
        }

        protected virtual bool TryGetHandleForPlayer(MontagePlayer player, out MontageHandle handle)
        {
            if (player == null)
            {
                handle = MontageHandle.Invalid;
                return false;
            }

            // 严格按照 Play 返回主句柄的优先级顺序查找：FullBody (1) -> UpperBody (0) -> Additive (2)
            ReadOnlySpan<int> priorityOrder = stackalloc int[] { 1, 0, 2 };
            for (int p = 0; p < priorityOrder.Length; p++)
            {
                int layerIdx = priorityOrder[p];
                if (layerIdx < 0 || layerIdx >= _layerStates.Count) continue;

                var layer = _layerStates[layerIdx];
                if (layer.Slot0.Player == player)
                {
                    handle = new MontageHandle(this, layerIdx, 0, layer.Slot0.Generation);
                    return true;
                }
                if (layer.Slot1.Player == player)
                {
                    handle = new MontageHandle(this, layerIdx, 1, layer.Slot1.Generation);
                    return true;
                }
            }

            foreach (var kvp in _standalonePlayers)
            {
                if (kvp.Value.player == player)
                {
                    handle = new MontageHandle(this, -1, kvp.Key, kvp.Value.generation);
                    return true;
                }
            }

            handle = MontageHandle.Invalid;
            return false;
        }

        #endregion

        #region Handle Safety & Generation Dispatchers

        protected virtual bool TryGetValidPlayer(int layerIndex, int slotIndex, int generation, out MontagePlayer player)
        {
            player = null;
            if (layerIndex == -1)
            {
                if (_standalonePlayers.TryGetValue(slotIndex, out var info) && info.generation == generation)
                {
                    player = info.player;
                    return player != null && !player.IsFinished;
                }
                return false;
            }

            if (layerIndex < 0 || layerIndex >= _layerStates.Count) return false;

            var layer = _layerStates[layerIndex];
            var slot = (slotIndex == 0) ? layer.Slot0 : layer.Slot1;

            if (!slot.IsOccupied || slot.Generation != generation || slot.Player == null || slot.Player.IsFinished)
            {
                return false;
            }

            player = slot.Player;
            return true;
        }

        protected virtual bool TryGetSlotPlayer(int layerIndex, int slotIndex, int generation, out MontagePlayer player)
        {
            player = null;
            if (layerIndex == -1)
            {
                if (_standalonePlayers.TryGetValue(slotIndex, out var info) && info.generation == generation)
                {
                    player = info.player;
                    return player != null;
                }
                return false;
            }

            if (layerIndex < 0 || layerIndex >= _layerStates.Count) return false;

            var layer = _layerStates[layerIndex];
            var slot = (slotIndex == 0) ? layer.Slot0 : layer.Slot1;

            if (!slot.IsOccupied || slot.Generation != generation || slot.Player == null)
            {
                return false;
            }

            player = slot.Player;
            return true;
        }

        internal bool IsHandleValid(int layerIndex, int slotIndex, int generation)
        {
            return TryGetValidPlayer(layerIndex, slotIndex, generation, out _);
        }

        internal bool IsHandlePlaying(int layerIndex, int slotIndex, int generation)
        {
            return TryGetValidPlayer(layerIndex, slotIndex, generation, out var player) && player.IsPlaying;
        }

        internal bool IsHandleStopping(int layerIndex, int slotIndex, int generation)
        {
            return TryGetValidPlayer(layerIndex, slotIndex, generation, out var player) && player.IsStopping;
        }

        internal bool IsHandleFinished(int layerIndex, int slotIndex, int generation)
        {
            if (layerIndex == -1)
            {
                if (_standalonePlayers.TryGetValue(slotIndex, out var info) && info.generation == generation)
                {
                    return info.player == null || info.player.IsFinished;
                }
                return true;
            }

            if (layerIndex < 0 || layerIndex >= _layerStates.Count) return true;

            var layer = _layerStates[layerIndex];
            var slot = (slotIndex == 0) ? layer.Slot0 : layer.Slot1;
            if (!slot.IsOccupied || slot.Generation != generation || slot.Player == null)
            {
                return true;
            }

            return slot.Player.IsFinished;
        }

        internal bool IsHandlePaused(int layerIndex, int slotIndex, int generation)
        {
            return TryGetValidPlayer(layerIndex, slotIndex, generation, out var player) && player.IsPaused;
        }

        internal float GetHandleWeight(int layerIndex, int slotIndex, int generation)
        {
            if (layerIndex >= 0 && layerIndex < _layerStates.Count)
            {
                var layer = _layerStates[layerIndex];
                var slot = (slotIndex == 0) ? layer.Slot0 : layer.Slot1;
                if (slot.IsOccupied && slot.Generation == generation && slot.Player != null && !slot.Player.IsFinished)
                {
                    return slot.Weight;
                }
            }
            return 0f;
        }

        internal void SetHandleWeight(int layerIndex, int slotIndex, int generation, float weight)
        {
            if (TryGetValidPlayer(layerIndex, slotIndex, generation, out var player))
            {
                player.SetCustomWeight(weight);
            }
        }

        internal float GetHandleTotalDuration(int layerIndex, int slotIndex, int generation)
        {
            return TryGetSlotPlayer(layerIndex, slotIndex, generation, out var player) ? player.TotalDuration : 0f;
        }

        internal float GetHandleElapsedTime(int layerIndex, int slotIndex, int generation)
        {
            return TryGetSlotPlayer(layerIndex, slotIndex, generation, out var player) ? player.ElapsedTime : 0f;
        }

        internal int GetHandleSectionIndex(int layerIndex, int slotIndex, int generation)
        {
            return TryGetSlotPlayer(layerIndex, slotIndex, generation, out var player) ? player.CurrentSectionIndex : -1;
        }

        internal IMontageSequence GetHandleSourceSequence(int layerIndex, int slotIndex, int generation)
        {
            return TryGetSlotPlayer(layerIndex, slotIndex, generation, out var player) ? player.Sequence : null;
        }

        internal MontageSequenceSO GetHandleSourceAsset(int layerIndex, int slotIndex, int generation)
        {
            return TryGetSlotPlayer(layerIndex, slotIndex, generation, out var player) ? player.SourceAsset : null;
        }

        internal void JumpToSection(int layerIndex, int slotIndex, int generation, int sectionIndex)
        {
            if (TryGetValidPlayer(layerIndex, slotIndex, generation, out var player))
            {
                player.JumpToSection(sectionIndex);
            }
        }

        internal void JumpToTime(int layerIndex, int slotIndex, int generation, float targetTime)
        {
            if (TryGetValidPlayer(layerIndex, slotIndex, generation, out var player))
            {
                player.JumpToTime(targetTime);
            }
        }

        internal void EvaluateSectionProgress(int layerIndex, int slotIndex, int generation, int sectionIndex, float progress)
        {
            if (TryGetValidPlayer(layerIndex, slotIndex, generation, out var player))
            {
                player.EvaluateSectionProgress(sectionIndex, progress);
            }
        }

        internal void SyncSectionDuration(int layerIndex, int slotIndex, int generation, int sectionIndex, float targetDuration)
        {
            if (TryGetValidPlayer(layerIndex, slotIndex, generation, out var player))
            {
                player.SyncSectionDuration(sectionIndex, targetDuration);
            }
        }

        internal void SyncSection(int layerIndex, int slotIndex, int generation, int sectionIndex, float targetDuration, bool jumpImmediately)
        {
            if (TryGetValidPlayer(layerIndex, slotIndex, generation, out var player))
            {
                player.SyncSection(sectionIndex, targetDuration, jumpImmediately);
            }
        }

        internal void ClearSectionSync(int layerIndex, int slotIndex, int generation, int sectionIndex)
        {
            if (TryGetValidPlayer(layerIndex, slotIndex, generation, out var player))
            {
                player.ClearSectionSync(sectionIndex);
            }
        }

        internal int GetHandleSectionCount(int layerIndex, int slotIndex, int generation)
        {
            return TryGetValidPlayer(layerIndex, slotIndex, generation, out var player) ? player.SectionCount : 0;
        }

        internal bool HasHandleSections(int layerIndex, int slotIndex, int generation)
        {
            return TryGetValidPlayer(layerIndex, slotIndex, generation, out var player) && player.HasSections;
        }

        internal bool HasHandleSection(int layerIndex, int slotIndex, int generation, int sectionIndex)
        {
            return TryGetValidPlayer(layerIndex, slotIndex, generation, out var player) && player.HasSection(sectionIndex);
        }

        internal void SetPlaybackRate(int layerIndex, int slotIndex, int generation, float rate)
        {
            if (TryGetValidPlayer(layerIndex, slotIndex, generation, out var player))
            {
                player.SetPlaybackRate(rate);
            }
        }

        internal void SetPaused(int layerIndex, int slotIndex, int generation, bool isPaused)
        {
            if (TryGetValidPlayer(layerIndex, slotIndex, generation, out var player))
            {
                player.SetPaused(isPaused);
            }
        }

        internal void StopHandle(int layerIndex, int slotIndex, int generation, float? blendOutTime)
        {
            if (TryGetValidPlayer(layerIndex, slotIndex, generation, out var player))
            {
                float duration = blendOutTime ?? 0.1f;
                for (int i = 0; i < _layerStates.Count; i++)
                {
                    var layer = _layerStates[i];
                    if (layer.Slot0.Player == player)
                    {
                        layer.Slot0.StartFadeOut(duration);
                    }
                    if (layer.Slot1.Player == player)
                    {
                        layer.Slot1.StartFadeOut(duration);
                    }
                }

                if (player.IsPlaying)
                {
                    player.Stop(duration);
                }
            }
        }

        #endregion
    }

#pragma warning disable CS0618
    /// <summary>
    /// Backward-compatibility alias for MontageController.
    /// </summary>
    [Obsolete("MontageCoordinator has been renamed to MontageController. Please use MontageController instead.")]
    [AddComponentMenu("")]
    public class MontageCoordinator : MontageController
    {
    }
#pragma warning restore CS0618
}
