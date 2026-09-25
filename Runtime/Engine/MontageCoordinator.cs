using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 单个动画图层的配置数据（向后兼容保留定义）。
    /// </summary>
    [Serializable]
    public struct MontageLayerConfig
    {
        [Tooltip("该图层的骨骼遮罩 (AvatarMask)。")]
        public AvatarMask AvatarMask;

        [Tooltip("是否为叠加层 (Additive)。")]
        public bool IsAdditive;

        [Range(0f, 1f)]
        [Tooltip("图层基础权重倍率。")]
        public float LayerWeight;
    }

    /// <summary>
    /// 蒙太奇角色驱动协调器组件（MonoBehaviour）。
    /// 基于 Unity Playables API 构建四层混音拓扑（Locomotion -> UpperBody -> FullBody -> Additive），
    /// 内置通用 Humanoid 上半身遮罩与脊柱朝向补偿，统一管理角色的蒙太奇播放与 Root Motion 分发。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Cwcbb/Montage/Montage Coordinator")]
    public class MontageCoordinator : MonoBehaviour
    {
        #region 内部数据结构

        private class SlotState
        {
            public readonly int SlotIndex;
            public AnimationMixerPlayable SlotMixer;
            public readonly List<AnimationClipPlayable> SegmentPlayables = new();
            public MontagePlayer Player;
            public float Weight;
            public int Generation { get; set; } = 1;
            public bool HasActiveAnimation { get; set; }
            public float CurrentSegmentWeightMultiplier { get; set; } = 1.0f;

            public readonly List<int> TempEvalIndices = new();
            public readonly List<float> TempEvalTimes = new();
            public readonly List<float> TempEvalWeights = new();

            public bool IsOccupied => Player != null;

            public SlotState(int slotIndex)
            {
                SlotIndex = slotIndex;
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
                HasActiveAnimation = false;
                CurrentSegmentWeightMultiplier = 1.0f;
                TempEvalIndices.Clear();
                TempEvalTimes.Clear();
                TempEvalWeights.Clear();
            }
        }

        private class LayerRuntimeState
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

        #region Inspector 字段

        [Header("Animator Binding")]
        [Tooltip("绑定的目标 Animator 组件。若为空将在自身及子物体中自动查找。")]
        [SerializeField] private Animator _animator;

        [Header("Humanoid Layer Settings")]
        [Tooltip("可选自定义上半身骨骼遮罩（若为空则自动使用通用 Humanoid 标准上半身遮罩，实现开箱即用）。")]
        [SerializeField] private AvatarMask _customUpperBodyMask;

        [Header("Playback Defaults")]
        [Tooltip("全局播放速率缩放倍率。")]
        [Min(0.0001f)]
        [SerializeField] private float _globalPlaybackRate = 1.0f;

        #endregion

        #region 私有字段

        private PlayableGraph _playableGraph;
        private AnimationLayerMixerPlayable _topLevelMixer;
        private AnimatorControllerPlayable _locomotionPlayable;
        private RuntimeAnimatorController _originalController;
        private MontageAnimatorDispatcher _animatorDispatcher;

        private readonly List<LayerRuntimeState> _layerStates = new(3);
        private readonly Transform[] _cachedBones = new Transform[MontageBoneUtility.BONE_COUNT];
        private readonly HashSet<MontagePlayer> _tickablePlayers = new(4);
        private IMontageRootMotionReceiver _cachedReceiver;

        private bool _isGraphInitialized;

        #endregion

        #region 公共属性

        /// <summary>
        /// 绑定的 Animator 组件。
        /// </summary>
        public Animator TargetAnimator => _animator;

        /// <summary>
        /// 当前是否有任一图层处于蒙太奇播放中（包含淡入、稳定播放及淡出期）。
        /// </summary>
        public bool IsPlayingMontage => IsPlayingAnyMontage;

        /// <summary>
        /// 当前是否有任一图层处于蒙太奇播放中。
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
        /// 当前主层（优先 FullBody，其次 UpperBody/Additive）活跃的蒙太奇播放句柄。
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
        /// 全局播放速率缩放倍率。
        /// </summary>
        public float GlobalPlaybackRate
        {
            get => _globalPlaybackRate;
            set => _globalPlaybackRate = Mathf.Max(0.0001f, value);
        }

        #endregion

        #region 公共事件

        /// <summary>
        /// 当调度器计算出当帧有效根运动位移增量与旋转增量时触发。
        /// 外部移动系统（如角色控制器）可订阅此事件进行物理位移消费与二次过滤。
        /// </summary>
        public event Action<Vector3, Quaternion> OnRootMotionDelta;

        /// <summary>
        /// 当蒙太奇开始播放时触发，提供对应的播放智能句柄。
        /// </summary>
        public event Action<MontageHandle> OnMontageStarted;

        /// <summary>
        /// 当蒙太奇完成或被中断结束时触发，提供对应的播放智能句柄。
        /// </summary>
        public event Action<MontageHandle> OnMontageEnded;

        /// <summary>
        /// 当蒙太奇跨越物理分段切分点时触发，提供对应的播放智能句柄及目标分段索引。
        /// </summary>
        public event Action<MontageHandle, int> OnSectionChanged;

        #endregion

        #region Unity 生命周期

        private void Awake()
        {
            EnsureAnimator();
            _cachedReceiver = GetComponent<IMontageRootMotionReceiver>();
            InitializePlayableGraph();
        }

        private void Update()
        {
            if (!_isGraphInitialized || !_playableGraph.IsValid())
            {
                return;
            }

            float effectiveDelta = Time.deltaTime * _globalPlaybackRate;

            // 1. 先更新各个动作图层中的双缓冲 Slot 播放器与 CrossFade 权重
            UpdateLayers(effectiveDelta);

            // 2. 后手动推进 PlayableGraph 采样（驱动子层级 Animator 采样并在 OnAnimatorMove 中分发 Root Motion）
            _playableGraph.Evaluate(effectiveDelta);

            // 3. 上半身基准骨骼相对根节点解耦姿态修正（消除下半身骨盆奔跑倾斜与晃动，还原侧身/平刺等源动画真实朝向）
            ApplyUpperBodySpineDecoupling();
        }

        private void OnDestroy()
        {
            if (_animatorDispatcher != null)
            {
                _animatorDispatcher.Unbind();
            }

            // 强制终止所有 Layer 的所有 Slot 中的 Player，确保动作块绝对触发 OnExit
            for (int i = 0; i < _layerStates.Count; i++)
            {
                var layer = _layerStates[i];
                if (layer.Slot0.IsOccupied)
                {
                    layer.Slot0.Player?.Terminate();
                    UnbindPlayerEvents(layer.Slot0.Player);
                    layer.Slot0.Reset();
                }
                if (layer.Slot1.IsOccupied)
                {
                    layer.Slot1.Player?.Terminate();
                    UnbindPlayerEvents(layer.Slot1.Player);
                    layer.Slot1.Reset();
                }
            }

            CleanupPlayableGraph();

            // 还原 Animator 的 RuntimeAnimatorController
            if (_animator != null && _originalController != null)
            {
                _animator.runtimeAnimatorController = _originalController;
            }
        }

        #endregion

        #region 公共方法 (播放与停止 API)

        /// <summary>
        /// 播放指定的蒙太奇配置资产。
        /// 自动根据资产配置在 UpperBody、FullBody 与 Additive 通道分发插槽，
        /// 执行平滑双缓冲 CrossFade 过渡与空隙自然交还控制权。
        /// </summary>
        /// <param name="montage">蒙太奇配置资产</param>
        /// <param name="customBlendInTime">自定义淡入时长（可选）</param>
        /// <param name="customBlendInCurve">自定义淡入曲线（可选）</param>
        /// <returns>主导图层对应的蒙太奇播放智能结构体句柄</returns>
        public MontageHandle Play(
            MontageSequenceSO montage,
            float? customBlendInTime = null,
            AnimationCurve customBlendInCurve = null)
        {
            if (montage == null)
            {
                Debug.LogWarning($"[MontageCoordinator] 物体 '{gameObject.name}' 无法播放：montage 为空。");
                return MontageHandle.Invalid;
            }

            if (!_isGraphInitialized)
            {
                InitializePlayableGraph();
            }

            float blendInDuration = customBlendInTime ?? montage.DefaultBlendInTime;

            // 1. 实例化单一权威时钟源播放器
            var player = new MontagePlayer(
                montage,
                gameObject,
                _animator,
                blendInDuration,
                customBlendInCurve,
                isPreview: false,
                coordinator: this);

            // 注册生命周期回调（单个 Player 实例仅注册一次）
            player.OnSectionEntered += HandleSectionEntered;
            player.OnFinished += HandlePlayerEnded;
            player.OnInterrupted += HandlePlayerEnded;

            bool hasFullBody = montage.HasAnyAnimationInChannel(MontageLayerChannel.FullBody);
            bool hasUpperBody = montage.HasAnyAnimationInChannel(MontageLayerChannel.UpperBody);
            bool hasAdditive = montage.HasAnyAnimationInChannel(MontageLayerChannel.Additive);

            // 若所有通道均无动画片段但配置了表现轨道，保底在 FullBody 层挂载播放器以驱动事件时钟
            if (!hasFullBody && !hasUpperBody && !hasAdditive)
            {
                hasFullBody = true;
            }

            MontageHandle mainHandle = MontageHandle.Invalid;

            // 2. 全身动作优先级：播放全身独占动作时，通知上半身动作平滑淡出
            if (hasFullBody)
            {
                StopActiveSlot(_layerStates[0], blendInDuration);
            }

            // 3. 通道分发挂载
            if (hasFullBody)
            {
                var handle = SetupLayerSlot(_layerStates[1], montage.FullBodySegments, player, blendInDuration, montage.IsFootIK);
                if (!mainHandle.IsValid) mainHandle = handle;
            }

            if (hasUpperBody)
            {
                var handle = SetupLayerSlot(_layerStates[0], montage.UpperBodySegments, player, blendInDuration, montage.IsFootIK);
                if (!mainHandle.IsValid) mainHandle = handle;
            }

            if (hasAdditive)
            {
                var handle = SetupLayerSlot(_layerStates[2], montage.AdditiveSegments, player, blendInDuration, false);
                if (!mainHandle.IsValid) mainHandle = handle;
            }

            // 4. 首帧对齐：在挂载完成后立即执行一次零时间步更新，确保图层权重与 Mixer 拓扑立即对齐生效
            UpdateLayers(0f);

            if (mainHandle.IsValid)
            {
                OnMontageStarted?.Invoke(mainHandle);
            }

            return mainHandle;
        }

        /// <summary>
        /// 获取指定通道当前正在播放的活跃蒙太奇句柄。
        /// </summary>
        /// <param name="channel">目标通道枚举</param>
        public MontageHandle GetActiveHandle(MontageLayerChannel channel)
        {
            int index = (int)channel;
            return GetActiveHandle(index);
        }

        /// <summary>
        /// 获取指定图层索引当前正在播放的活跃蒙太奇句柄。
        /// </summary>
        /// <param name="layerIndex">图层索引（0: UpperBody, 1: FullBody, 2: Additive）</param>
        public MontageHandle GetActiveHandle(int layerIndex = 1)
        {
            if (layerIndex < 0 || layerIndex >= _layerStates.Count) return MontageHandle.Invalid;
            var layer = _layerStates[layerIndex];
            if (layer.ActiveSlotIndex < 0) return MontageHandle.Invalid;
            var slot = (layer.ActiveSlotIndex == 0) ? layer.Slot0 : layer.Slot1;
            if (!slot.IsOccupied || slot.Player == null || slot.Player.IsFinished) return MontageHandle.Invalid;
            return new MontageHandle(this, layerIndex, slot.SlotIndex, slot.Generation);
        }

        /// <summary>
        /// 查询指定通道当前是否正处于蒙太奇播放状态。
        /// </summary>
        public bool IsPlayingChannel(MontageLayerChannel channel)
        {
            return IsPlayingLayer((int)channel);
        }

        /// <summary>
        /// 查询指定图层当前是否正处于蒙太奇播放状态。
        /// </summary>
        /// <param name="layerIndex">图层索引</param>
        public bool IsPlayingLayer(int layerIndex)
        {
            if (layerIndex < 0 || layerIndex >= _layerStates.Count) return false;
            var layer = _layerStates[layerIndex];
            if (layer.ActiveSlotIndex < 0) return false;
            var slot = (layer.ActiveSlotIndex == 0) ? layer.Slot0 : layer.Slot1;
            return slot.IsOccupied && slot.Player != null && !slot.Player.IsFinished && (slot.Player.IsPlaying || slot.Weight > 0.0001f);
        }

        /// <summary>
        /// 停止指定通道正在播放的蒙太奇。
        /// </summary>
        public void StopChannel(MontageLayerChannel channel, float blendOutTime = 0.15f)
        {
            StopLayer((int)channel, blendOutTime);
        }

        /// <summary>
        /// 停止指定图层正在播放的蒙太奇。
        /// </summary>
        /// <param name="layerIndex">图层索引</param>
        /// <param name="blendOutTime">淡出时间</param>
        public void StopLayer(int layerIndex, float blendOutTime = 0.15f)
        {
            if (layerIndex < 0 || layerIndex >= _layerStates.Count)
            {
                Debug.LogWarning($"[MontageCoordinator] 图层索引 {layerIndex} 无效 (有效范围: 0 ~ {_layerStates.Count - 1})。");
                return;
            }

            var layer = _layerStates[layerIndex];
            if (layer.Slot0.IsOccupied && layer.Slot0.Player.IsPlaying)
            {
                layer.Slot0.Player.Stop(blendOutTime);
            }
            if (layer.Slot1.IsOccupied && layer.Slot1.Player.IsPlaying)
            {
                layer.Slot1.Player.Stop(blendOutTime);
            }
        }

        /// <summary>
        /// 停止所有图层当前正在播放的蒙太奇。
        /// </summary>
        /// <param name="blendOutTime">淡出时间</param>
        public void StopAll(float blendOutTime = 0.15f)
        {
            for (int i = 0; i < _layerStates.Count; i++)
            {
                StopLayer(i, blendOutTime);
            }
        }

        /// <summary>
        /// 设置指定图层的运行时动态混合权重倍率 [0.0, 1.0]。
        /// </summary>
        /// <param name="layerIndex">图层索引（0: UpperBody, 1: FullBody, 2: Additive）</param>
        /// <param name="weight">权重值 [0.0, 1.0]</param>
        public void SetLayerWeight(int layerIndex, float weight)
        {
            if (layerIndex < 0 || layerIndex >= _layerStates.Count) return;
            _layerStates[layerIndex].LayerWeight = Mathf.Clamp01(weight);
        }

        /// <summary>
        /// 获取指定图层的运行时动态混合权重倍率。
        /// </summary>
        /// <param name="layerIndex">图层索引（0: UpperBody, 1: FullBody, 2: Additive）</param>
        public float GetLayerWeight(int layerIndex)
        {
            if (layerIndex < 0 || layerIndex >= _layerStates.Count) return 1.0f;
            return _layerStates[layerIndex].LayerWeight;
        }

        /// <summary>
        /// 设置指定通道的运行时动态混合权重倍率 [0.0, 1.0]。
        /// </summary>
        /// <param name="channel">目标通道枚举</param>
        /// <param name="weight">权重值 [0.0, 1.0]</param>
        public void SetChannelWeight(MontageLayerChannel channel, float weight)
        {
            SetLayerWeight((int)channel, weight);
        }

        /// <summary>
        /// 获取指定通道的运行时动态混合权重倍率。
        /// </summary>
        /// <param name="channel">目标通道枚举</param>
        public float GetChannelWeight(MontageLayerChannel channel)
        {
            return GetLayerWeight((int)channel);
        }

        /// <summary>
        /// 在运行时动态更换底层 Locomotion 状态机控制器。
        /// </summary>
        /// <param name="newController">新的 RuntimeAnimatorController 实例</param>
        public void SetLocomotionAnimatorController(RuntimeAnimatorController newController)
        {
            if (newController == null)
            {
                Debug.LogWarning($"[MontageCoordinator] 物体 '{gameObject.name}' 无法设置空的 Locomotion Controller。");
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

            // 断开并销毁旧的 Locomotion Playable
            _topLevelMixer.DisconnectInput(0);
            if (_locomotionPlayable.IsValid())
            {
                _playableGraph.DestroyPlayable(_locomotionPlayable);
            }

            // 创建并重连新的 Locomotion Playable
            _locomotionPlayable = AnimatorControllerPlayable.Create(_playableGraph, newController);
            _topLevelMixer.ConnectInput(0, _locomotionPlayable, 0);
            _topLevelMixer.SetInputWeight(0, 1.0f);
        }

        /// <summary>
        /// 重置底层 Locomotion 为最初绑定的 Controller。
        /// </summary>
        public void ResetLocomotionAnimatorController()
        {
            if (_originalController != null)
            {
                SetLocomotionAnimatorController(_originalController);
            }
            else
            {
                Debug.LogWarning($"[MontageCoordinator] 物体 '{gameObject.name}' 未记录初始 Controller，无法重置。");
            }
        }

        /// <summary>
        /// 获取当前角色绑定的目标核心大骨骼 Transform（O(1) 零 GC 极速读取）。
        /// </summary>
        /// <param name="targetBone">核心大骨骼枚举</param>
        /// <returns>目标骨骼 Transform，保底回退返回角色自身 transform</returns>
        public Transform GetTargetBone(MontageTargetBone targetBone)
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

        #region 初始化与图拓扑构建 (固定人形四层)

        private void EnsureAnimator()
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

            Debug.LogError($"[MontageCoordinator] 物体 '{gameObject.name}' 未显式配置 Animator，且在自身或第一层子物体中均未找到 Animator 组件！", this);
        }

        private void SetupAnimatorBinding()
        {
            if (_animator == null) return;

            _originalController = _animator.runtimeAnimatorController;
            MontageBoneUtility.ResolveBones(gameObject, _animator, _cachedBones);

            _animatorDispatcher = _animator.GetComponent<MontageAnimatorDispatcher>();
            if (_animatorDispatcher == null)
            {
                _animatorDispatcher = _animator.gameObject.AddComponent<MontageAnimatorDispatcher>();
            }
            _animatorDispatcher.Bind(HandleAnimatorMove);
        }

        private void InitializePlayableGraph()
        {
            if (_animator == null)
            {
                Debug.LogError($"[MontageCoordinator] 物体 '{gameObject.name}' 缺少 Animator 组件，初始化 PlayableGraph 失败。");
                return;
            }

            _playableGraph = PlayableGraph.Create($"CwcMontage_{gameObject.name}");
            _playableGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

            var playableOutput = AnimationPlayableOutput.Create(_playableGraph, "MontageOutput", _animator);

            // 固定四层拓扑：
            // Input 0: Locomotion (基础状态机)
            // Input 1: UpperBody (上半身配合移动，内置 Humanoid 遮罩)
            // Input 2: FullBody (全身独占动作，霸权覆盖)
            // Input 3: Additive (受击/抖动叠加，Additive = true)
            _topLevelMixer = AnimationLayerMixerPlayable.Create(_playableGraph, 4);

            _animator.runtimeAnimatorController = null;
            _animator.applyRootMotion = true;

            if (_originalController != null)
            {
                _locomotionPlayable = AnimatorControllerPlayable.Create(_playableGraph, _originalController);
                _topLevelMixer.ConnectInput(0, _locomotionPlayable, 0);
                _topLevelMixer.SetInputWeight(0, 1.0f);
            }
            else
            {
                Debug.LogWarning($"[MontageCoordinator] 物体 '{gameObject.name}' 的 Animator 尚未分配 RuntimeAnimatorController，Locomotion 基础层输入为空。");
                _locomotionPlayable = default;
                _topLevelMixer.SetInputWeight(0, 0.0f);
            }

            _layerStates.Clear();

            // 1. UpperBody (Layer 0, Top Input 1)
            var upperLayer = CreateLayerState(MontageLayerChannel.UpperBody, 0, 1);
            var upperMask = _customUpperBodyMask != null ? _customUpperBodyMask : MontageMaskUtility.GetOrCreateHumanoidUpperBodyMask();
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

            playableOutput.SetSourcePlayable(_topLevelMixer);
            _playableGraph.Play();
            _isGraphInitialized = true;
        }

        private LayerRuntimeState CreateLayerState(MontageLayerChannel channel, int layerIndex, int topInputIndex)
        {
            var state = new LayerRuntimeState(channel, layerIndex, topInputIndex);
            var layerMixer = AnimationMixerPlayable.Create(_playableGraph, 2);
            state.LayerMixer = layerMixer;

            _topLevelMixer.ConnectInput(topInputIndex, layerMixer, 0);
            _topLevelMixer.SetInputWeight(topInputIndex, 0.0f);
            return state;
        }

        private void CleanupPlayableGraph()
        {
            if (_playableGraph.IsValid())
            {
                _playableGraph.Destroy();
                _playableGraph = default;
            }

            _isGraphInitialized = false;
        }

        #endregion

        #region 插槽配置与多通道分发辅助

        private void StopActiveSlot(LayerRuntimeState layer, float blendOutDuration)
        {
            if (layer.ActiveSlotIndex < 0) return;
            var slot = (layer.ActiveSlotIndex == 0) ? layer.Slot0 : layer.Slot1;
            if (slot.IsOccupied && slot.Player.IsPlaying)
            {
                slot.Player.Stop(blendOutDuration);
            }
        }

        private MontageHandle SetupLayerSlot(
            LayerRuntimeState layerState,
            List<MontageAnimationSegment> segments,
            MontagePlayer player,
            float blendInDuration,
            bool isFootIK)
        {
            int newSlotIndex = (layerState.ActiveSlotIndex == 0) ? 1 : 0;
            int oldSlotIndex = (newSlotIndex == 0) ? 1 : 0;

            var newSlot = (newSlotIndex == 0) ? layerState.Slot0 : layerState.Slot1;
            var oldSlot = (oldSlotIndex == 0) ? layerState.Slot0 : layerState.Slot1;

            if (oldSlot.IsOccupied && oldSlot.Player.IsPlaying)
            {
                oldSlot.Player.Stop(blendInDuration);
            }

            if (newSlot.IsOccupied)
            {
                if (!newSlot.Player.IsFinished)
                {
                    newSlot.Player.Terminate();
                }
                UnbindPlayerEvents(newSlot.Player);
            }

            newSlot.Generation++;
            if (newSlot.Generation <= 0) newSlot.Generation = 1;

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

            newSlot.Weight = 0.0f;
            newSlot.HasActiveAnimation = false;
            newSlot.Player = player;
            layerState.ActiveSlotIndex = newSlotIndex;

            return new MontageHandle(this, layerState.LayerIndex, newSlotIndex, newSlot.Generation);
        }

        #endregion

        #region 图更新与通道动态混音计算 (空白区自然释放)

        private void UpdateLayers(float deltaTime)
        {
            // 1. 收集并统一推进所有活跃 Player 的单一权威时钟与动作块扫掠（确保多通道共用时钟只推进一次）
            _tickablePlayers.Clear();
            for (int i = 0; i < _layerStates.Count; i++)
            {
                var layer = _layerStates[i];
                if (layer.Slot0.IsOccupied && layer.Slot0.Player != null) _tickablePlayers.Add(layer.Slot0.Player);
                if (layer.Slot1.IsOccupied && layer.Slot1.Player != null) _tickablePlayers.Add(layer.Slot1.Player);
            }

            foreach (var p in _tickablePlayers)
            {
                p.Tick(deltaTime);
                p.SetCurrentWeight(0f);
            }

            // 2. 推进各图层 Slot 的骨骼姿态采样与动态权重归一化计算
            for (int i = 0; i < _layerStates.Count; i++)
            {
                var layer = _layerStates[i];
                var mixer = layer.LayerMixer;

                UpdateSlotClips(layer, layer.Slot0);
                UpdateSlotClips(layer, layer.Slot1);

                SlotState activeSlot = (layer.ActiveSlotIndex == 0) ? layer.Slot0 : layer.Slot1;
                SlotState fadingSlot = (layer.ActiveSlotIndex == 0) ? layer.Slot1 : layer.Slot0;

                // 【核心重构】：只有当前处于活跃动画片段覆盖内（HasActiveAnimation），才具有姿态权重！
                // 结合片段自身当前的淡入淡出（BlendIn/BlendOut）包络权重乘数，实现平滑进入与退出，杜绝姿态突变硬切！
                float activeRawWeight = (activeSlot.IsOccupied && activeSlot.HasActiveAnimation)
                    ? activeSlot.Player.CalculateTargetWeight() * activeSlot.CurrentSegmentWeightMultiplier
                    : 0f;
                float fadingRawWeight = (fadingSlot.IsOccupied && fadingSlot.HasActiveAnimation)
                    ? fadingSlot.Player.CalculateTargetWeight() * fadingSlot.CurrentSegmentWeightMultiplier
                    : 0f;

                float fadingWeight = Mathf.Min(fadingRawWeight, Mathf.Max(0f, 1f - activeRawWeight));
                activeSlot.Weight = activeRawWeight;
                fadingSlot.Weight = fadingWeight;

                if (activeSlot.IsOccupied && activeSlot.Player != null)
                {
                    float maxW = Mathf.Max(activeSlot.Player.CurrentWeight, activeRawWeight);
                    activeSlot.Player.SetCurrentWeight(maxW);
                }
                if (fadingSlot.IsOccupied && fadingSlot.Player != null)
                {
                    float maxW = Mathf.Max(fadingSlot.Player.CurrentWeight, fadingWeight);
                    fadingSlot.Player.SetCurrentWeight(maxW);
                }

                float activeAssetWeight = (activeSlot.IsOccupied && activeSlot.Player?.Sequence != null)
                    ? activeSlot.Player.Sequence.GetChannelWeight(layer.Channel)
                    : 1.0f;
                float fadingAssetWeight = (fadingSlot.IsOccupied && fadingSlot.Player?.Sequence != null)
                    ? fadingSlot.Player.Sequence.GetChannelWeight(layer.Channel)
                    : 1.0f;

                float effectiveActiveWeight = activeRawWeight * activeAssetWeight;
                float effectiveFadingWeight = fadingWeight * fadingAssetWeight;

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

                // 更新 TopLevelMixer 输入权重（结合蒙太奇资产层权重与运行时动态层权重倍率）
                float finalTopLayerWeight = combinedWeight * layer.LayerWeight;
                _topLevelMixer.SetInputWeight(layer.TopLevelInputIndex, Mathf.Clamp01(finalTopLayerWeight));

                // 释放彻底结束且权重归零的 Slot
                CheckAndReleaseSlot(layer.Slot0, mixer);
                CheckAndReleaseSlot(layer.Slot1, mixer);
            }
        }

        private void UpdateSlotClips(LayerRuntimeState layer, SlotState slot)
        {
            if (!slot.IsOccupied)
            {
                slot.Weight = 0f;
                slot.HasActiveAnimation = false;
                slot.CurrentSegmentWeightMultiplier = 0f;
                return;
            }

            var player = slot.Player;

            if (slot.SlotMixer.IsValid() && slot.SegmentPlayables.Count > 0)
            {
                player.EvaluateChannelSegments(layer.Channel, slot.TempEvalIndices, slot.TempEvalTimes, slot.TempEvalWeights);
                slot.HasActiveAnimation = (slot.TempEvalIndices.Count > 0);

                float totalSegWeight = 0f;
                for (int w = 0; w < slot.TempEvalWeights.Count; w++)
                {
                    totalSegWeight += slot.TempEvalWeights[w];
                }
                slot.CurrentSegmentWeightMultiplier = Mathf.Clamp01(totalSegWeight);

                int count = slot.SegmentPlayables.Count;
                for (int i = 0; i < count; i++)
                {
                    slot.SlotMixer.SetInputWeight(i, 0.0f);
                }

                // 【核心修复】：将片段权重归一化输入给 SlotMixer，确保 SlotMixer 内部总和恒为 1.0f！
                // 绝对杜绝 AnimationMixerPlayable 因总权重不足 1.0f 自动混入未初始化的 BindPose 0 姿态（半蹲/蜷缩贴地）！
                for (int k = 0; k < slot.TempEvalIndices.Count; k++)
                {
                    int segIdx = slot.TempEvalIndices[k];
                    if (segIdx >= 0 && segIdx < count)
                    {
                        var cp = slot.SegmentPlayables[segIdx];
                        if (cp.IsValid())
                        {
                            cp.SetTime(slot.TempEvalTimes[k]);
                            cp.SetSpeed(1.0f);
                        }
                        float normalizedW = totalSegWeight > 0.0001f ? (slot.TempEvalWeights[k] / totalSegWeight) : 0f;
                        slot.SlotMixer.SetInputWeight(segIdx, normalizedW);
                    }
                }
            }
            else
            {
                slot.HasActiveAnimation = false;
                slot.CurrentSegmentWeightMultiplier = 0f;
            }
        }

        private void CheckAndReleaseSlot(SlotState slot, AnimationMixerPlayable mixer)
        {
            if (!slot.IsOccupied) return;

            var player = slot.Player;
            if (player.IsFinished && slot.Weight <= 0.0001f)
            {
                slot.DestroyPlayables(_playableGraph, mixer);
                UnbindPlayerEvents(player);
                slot.Reset();
            }
        }

        private void ApplyUpperBodySpineDecoupling()
        {
            if (_animator == null || !_animator.isHuman || _layerStates.Count <= 0) return;

            // 0. 若全身动作（FullBody）正在播放且具有有效权重，优先保持全身动作姿态，不执行 Spine 局部朝向修正
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

            // 严格采用 Animator 的 Transform 作为根参考系，确保与 Dummy 采样基准完全处于同一局部坐标空间
            Transform root = _animator != null ? _animator.transform : transform;
            Transform hips = _cachedBones[(int)MontageTargetBone.Hips];
            Transform spine = _cachedBones[(int)MontageTargetBone.Spine];
            if (root == null || hips == null || spine == null) return;

            // 1. 动态获取源动画在当前时刻基准骨骼相对根节点的合成朝向 Q_Target
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

            // 2. 核心解耦反解：计算 Spine 在当前 Hips 下的基础局部旋转，使得 Spine 世界朝向精确锁定为角色根空间下的 targetSpineInRoot
            Quaternion targetSpineWorld = root.rotation * targetSpineInRoot;
            Quaternion decoupledLocal = Quaternion.Inverse(hips.rotation) * targetSpineWorld;

            // 3. 复合叠加层（Additive）姿态增量：确保解耦行为只限于 UpperBody，Additive 的受击/开火抖动平滑叠加在解耦姿态之上，杜绝脊柱断裂
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

            // 4. 结合当前 UpperBody 层的实际混合权重平滑插值（处理淡入、淡出与空白区过渡，0 抽搐）
            float blendWeight = Mathf.Clamp01(activeSlot.Weight * montage.UpperBodyWeight * upperLayer.LayerWeight);
            spine.localRotation = Quaternion.Slerp(spine.localRotation, finalDecoupledLocal, blendWeight);
        }

        #endregion

        #region Root Motion 采样与解耦分发

        private void HandleAnimatorMove(Vector3 deltaPosition, Quaternion deltaRotation)
        {
            // FullBody (Layer 1) 拥有对 Root Motion 的最高主导权
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

            if (dominantPlayer != null && dominantPlayer.SourceAsset != null)
            {
                var so = dominantPlayer.SourceAsset;

                if (so.ApplyHorizontalRootMotion)
                {
                    appliedDeltaPos.x = deltaPosition.x;
                    appliedDeltaPos.z = deltaPosition.z;
                }

                if (so.ApplyVerticalRootMotion)
                {
                    appliedDeltaPos.y = deltaPosition.y;
                }

                appliedDeltaRot = so.ApplyRotationRootMotion ? deltaRotation : Quaternion.identity;
            }
            else
            {
                // 无活跃全身蒙太奇时，直接放行底层 Locomotion 的原生 Root Motion
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

        #region 内部事件监听处理

        private void HandleSectionEntered(MontagePlayer player, int sectionIndex)
        {
            if (TryGetHandleForPlayer(player, out var handle))
            {
                OnSectionChanged?.Invoke(handle, sectionIndex);
            }
        }

        private void HandlePlayerEnded(MontagePlayer player)
        {
            if (TryGetHandleForPlayer(player, out var handle))
            {
                OnMontageEnded?.Invoke(handle);
            }

            UnbindPlayerEvents(player);
        }

        private void UnbindPlayerEvents(MontagePlayer player)
        {
            if (player == null) return;

            player.OnSectionEntered -= HandleSectionEntered;
            player.OnFinished -= HandlePlayerEnded;
            player.OnInterrupted -= HandlePlayerEnded;
        }

        private bool TryGetHandleForPlayer(MontagePlayer player, out MontageHandle handle)
        {
            if (player == null)
            {
                handle = MontageHandle.Invalid;
                return false;
            }

            for (int i = 0; i < _layerStates.Count; i++)
            {
                var layer = _layerStates[i];
                if (layer.Slot0.Player == player)
                {
                    handle = new MontageHandle(this, i, 0, layer.Slot0.Generation);
                    return true;
                }
                if (layer.Slot1.Player == player)
                {
                    handle = new MontageHandle(this, i, 1, layer.Slot1.Generation);
                    return true;
                }
            }

            handle = MontageHandle.Invalid;
            return false;
        }

        #endregion

        #region 句柄安全分发与代际校验 (Handle Dispatchers)

        private bool TryGetValidPlayer(int layerIndex, int slotIndex, int generation, out MontagePlayer player)
        {
            player = null;
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

        private bool TryGetSlotPlayer(int layerIndex, int slotIndex, int generation, out MontagePlayer player)
        {
            player = null;
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
            return TryGetSlotPlayer(layerIndex, slotIndex, generation, out var player) && player.IsFinished;
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
                player.Stop(blendOutTime);
            }
        }

        #endregion
    }
}
