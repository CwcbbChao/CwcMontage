using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 单个动画图层的配置数据。
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
    /// 蒙太奇全局驱动协调器组件（MonoBehaviour）。
    /// 基于 Unity Playables API 构建固定拓扑的双缓冲槽（Dual-Slot Ping-Pong）混音架构，
    /// 驱动各层蒙太奇交叉淡入淡出、时钟评估与根运动（Root Motion）解耦分发。
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
                TempEvalIndices.Clear();
                TempEvalTimes.Clear();
                TempEvalWeights.Clear();
            }
        }

        private class LayerRuntimeState
        {
            public readonly int LayerIndex;
            public AnimationMixerPlayable LayerMixer;
            public MontageLayerConfig Config;

            public readonly SlotState Slot0 = new(0);
            public readonly SlotState Slot1 = new(1);
            public int ActiveSlotIndex = -1;

            public LayerRuntimeState(int layerIndex, MontageLayerConfig config)
            {
                LayerIndex = layerIndex;
                Config = config;
            }
        }

        #endregion

        #region Inspector 字段

        [Header("Animator Binding")]
        [Tooltip("绑定的目标 Animator 组件。若为空将在自身及子物体中自动查找。")]
        [SerializeField] private Animator _animator;

        [Header("Layer Configurations")]
        [Tooltip("动作叠加/覆盖图层配置列表（Layer 1 ~ N，Layer 0 为底层状态机）。")]
        [SerializeField] private List<MontageLayerConfig> _layers = new();

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

        private readonly List<LayerRuntimeState> _layerStates = new(4);
        private readonly Transform[] _cachedBones = new Transform[MontageBoneUtility.BONE_COUNT];
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
        /// 当前主层（Layer 0）活跃的蒙太奇播放句柄。
        /// </summary>
        public MontageHandle ActiveHandle => GetActiveHandle(0);

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

            // 1. 先更新各个动作图层中的双缓冲 Slot 播放器与 CrossFade 权重（同步 Speed 与 Seek 时间）
            UpdateLayers(effectiveDelta);

            // 2. 后手动推进 PlayableGraph 采样（驱动子层级 Animator 采样并在 OnAnimatorMove 中分发 Root Motion）
            _playableGraph.Evaluate(effectiveDelta);
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
        /// 自动在指定图层进行双缓冲 CrossFade 平滑过渡。
        /// </summary>
        /// <param name="montage">蒙太奇配置资产</param>
        /// <param name="customBlendInTime">自定义淡入时长（可选）</param>
        /// <param name="customBlendInCurve">自定义淡入曲线（可选）</param>
        /// <returns>蒙太奇播放智能结构体句柄</returns>
        public MontageHandle Play(
            MontageSequenceSO montage,
            float? customBlendInTime = null,
            AnimationCurve customBlendInCurve = null)
        {
            if (montage == null || montage.AnimationSegments == null || montage.AnimationSegments.Count == 0)
            {
                Debug.LogWarning($"[MontageCoordinator] 物体 '{gameObject.name}' 无法播放：montage 为空或未配置任何动画片段 (AnimationSegments)。");
                return MontageHandle.Invalid;
            }

            if (!_isGraphInitialized)
            {
                InitializePlayableGraph();
            }

            int targetLayerIndex = montage.AnimationLayer;
            if (targetLayerIndex < 0 || targetLayerIndex >= _layerStates.Count)
            {
                Debug.LogWarning($"[MontageCoordinator] 物体 '{gameObject.name}' 播放蒙太奇 '{montage.name}' 时目标图层 {targetLayerIndex} 超出配置范围 (0 ~ {_layerStates.Count - 1})，将自动回退至 Layer 0。");
                targetLayerIndex = 0;
            }
            var layerState = _layerStates[targetLayerIndex];

            // 1. 确定双缓冲槽位 (Ping-Pong 交替切换)
            int newSlotIndex = (layerState.ActiveSlotIndex == 0) ? 1 : 0;
            int oldSlotIndex = (newSlotIndex == 0) ? 1 : 0;

            var newSlot = (newSlotIndex == 0) ? layerState.Slot0 : layerState.Slot1;
            var oldSlot = (oldSlotIndex == 0) ? layerState.Slot0 : layerState.Slot1;

            float blendInDuration = customBlendInTime ?? montage.DefaultBlendInTime;

            // 2. 将旧 Slot 置为主动打断淡出状态
            if (oldSlot.IsOccupied && oldSlot.Player.IsPlaying)
            {
                oldSlot.Player.Stop(blendInDuration);
            }

            // 3. 若新 Slot 仍然占有未完成的 Player（例如快速连续打断重用 Slot），强制 Terminate 并解绑事件
            if (newSlot.IsOccupied)
            {
                if (!newSlot.Player.IsFinished)
                {
                    newSlot.Player.Terminate();
                }
                UnbindPlayerEvents(newSlot.Player);
            }

            // 4. 代际版本号自增（每次分发新动画自增，旧句柄瞬间失效）
            newSlot.Generation++;
            if (newSlot.Generation <= 0) newSlot.Generation = 1;

            // 5. 清理新 Slot 原有遗留 Playable 连接
            newSlot.DestroyPlayables(_playableGraph, layerState.LayerMixer);

            // 6. 构建多片段内部 Mixer 并接入 Slot
            var segments = montage.AnimationSegments;
            int segCount = segments.Count;

            var slotMixer = AnimationMixerPlayable.Create(_playableGraph, segCount);
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
                    cp.SetApplyFootIK(montage.IsFootIK);
                    cp.SetSpeed(1.0f);
                    slotMixer.ConnectInput(s, cp, 0);
                }

                slotMixer.SetInputWeight(s, s == 0 ? 1.0f : 0.0f);
                newSlot.SegmentPlayables.Add(cp);
            }

            layerState.LayerMixer.ConnectInput(newSlot.SlotIndex, slotMixer, 0);
            layerState.LayerMixer.SetInputWeight(newSlot.SlotIndex, 0.0f);

            newSlot.Weight = 0.0f;

            // 7. 实例化运行时播放器
            var player = new MontagePlayer(
                montage,
                gameObject,
                _animator,
                blendInDuration,
                customBlendInCurve,
                isPreview: false,
                coordinator: this);

            newSlot.Player = player;
            layerState.ActiveSlotIndex = newSlotIndex;

            // 注册生命周期回调
            player.OnSectionEntered += HandleSectionEntered;
            player.OnFinished += HandlePlayerEnded;
            player.OnInterrupted += HandlePlayerEnded;

            var handle = new MontageHandle(this, targetLayerIndex, newSlotIndex, newSlot.Generation);
            OnMontageStarted?.Invoke(handle);
            return handle;
        }

        /// <summary>
        /// 获取指定图层当前正在播放的活跃蒙太奇句柄。
        /// </summary>
        /// <param name="layerIndex">图层索引（默认 0）</param>
        /// <returns>蒙太奇播放智能句柄（若该图层未在播放则返回 MontageHandle.Invalid）</returns>
        public MontageHandle GetActiveHandle(int layerIndex = 0)
        {
            if (layerIndex < 0 || layerIndex >= _layerStates.Count) return MontageHandle.Invalid;
            var layer = _layerStates[layerIndex];
            if (layer.ActiveSlotIndex < 0) return MontageHandle.Invalid;
            var slot = (layer.ActiveSlotIndex == 0) ? layer.Slot0 : layer.Slot1;
            if (!slot.IsOccupied || slot.Player == null || slot.Player.IsFinished) return MontageHandle.Invalid;
            return new MontageHandle(this, layerIndex, slot.SlotIndex, slot.Generation);
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

        #region 初始化与图拓扑构建 (固定双缓冲槽)

        private void EnsureAnimator()
        {
            // 1. 若 Inspector 中已显式配置，直接使用
            if (_animator != null)
            {
                SetupAnimatorBinding();
                return;
            }

            // 2. 查找自身
            _animator = GetComponent<Animator>();
            if (_animator != null)
            {
                SetupAnimatorBinding();
                return;
            }

            // 3. 仅查找第一层直接子物体（Direct Children，不递归孙代层级）
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

            // 4. 未找到则输出明确错误日志，严禁静默添加虚拟组件或盲目保底
            Debug.LogError($"[MontageCoordinator] 物体 '{gameObject.name}' 未显式配置 Animator，且在自身或第一层子物体中均未找到 Animator 组件！", this);
        }

        private void SetupAnimatorBinding()
        {
            if (_animator == null) return;

            _originalController = _animator.runtimeAnimatorController;
            MontageBoneUtility.ResolveBones(gameObject, _animator, _cachedBones);

            // 在 Animator 所在物体上挂载中介派发组件，拦截 Unity 原生 Root Motion 并转交 Coordinator
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

            // 构建总混音器 (Input 0 为 Locomotion，Input 1 ~ N 为动作层)
            int layerCount = _layers.Count > 0 ? _layers.Count : 1;
            int totalInputs = layerCount + 1;
            _topLevelMixer = AnimationLayerMixerPlayable.Create(_playableGraph, totalInputs);

            // 接入基础 Locomotion（若 Controller 为空则保持安全连接）
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

            // 构建动作层的固定双缓冲槽 Mixer 拓扑
            _layerStates.Clear();
            for (int i = 0; i < layerCount; i++)
            {
                var cfg = (i < _layers.Count) ? _layers[i] : new MontageLayerConfig { LayerWeight = 1.0f, IsAdditive = false };
                var layerState = new LayerRuntimeState(i, cfg);

                // 每个动作层固定创建拥有 2 个输入槽位的 AnimationMixerPlayable (Slot 0 和 Slot 1)
                var layerMixer = AnimationMixerPlayable.Create(_playableGraph, 2);
                layerState.LayerMixer = layerMixer;

                int topInputIndex = i + 1;
                _topLevelMixer.ConnectInput(topInputIndex, layerMixer, 0);
                _topLevelMixer.SetInputWeight(topInputIndex, 0.0f);
                _topLevelMixer.SetLayerAdditive((uint)topInputIndex, cfg.IsAdditive);

                if (cfg.AvatarMask != null)
                {
                    _topLevelMixer.SetLayerMaskFromAvatarMask((uint)topInputIndex, cfg.AvatarMask);
                }

                _layerStates.Add(layerState);
            }

            playableOutput.SetSourcePlayable(_topLevelMixer);
            _playableGraph.Play();
            _isGraphInitialized = true;
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

        #region 图更新与主从双缓冲混音计算 (Dominant Slot Blending)

        private void UpdateLayers(float deltaTime)
        {
            for (int i = 0; i < _layerStates.Count; i++)
            {
                var layer = _layerStates[i];
                var mixer = layer.LayerMixer;

                // 1. 推进各 Slot 的 Player 状态与底层 Playable 同步
                UpdateSlotPlayer(layer.Slot0, deltaTime);
                UpdateSlotPlayer(layer.Slot1, deltaTime);

                // 2. 主导槽与从属槽权重计算 (Dominant Slot Blending)
                SlotState activeSlot = (layer.ActiveSlotIndex == 0) ? layer.Slot0 : layer.Slot1;
                SlotState fadingSlot = (layer.ActiveSlotIndex == 0) ? layer.Slot1 : layer.Slot0;

                float activeRawWeight = activeSlot.IsOccupied ? activeSlot.Player.CalculateTargetWeight() : 0f;
                float fadingRawWeight = fadingSlot.IsOccupied ? fadingSlot.Player.CalculateTargetWeight() : 0f;
                float fadingWeight = Mathf.Min(fadingRawWeight, Mathf.Max(0f, 1f - activeRawWeight));

                activeSlot.Weight = activeRawWeight;
                fadingSlot.Weight = fadingWeight;

                if (activeSlot.IsOccupied) activeSlot.Player.SetCurrentWeight(activeRawWeight);
                if (fadingSlot.IsOccupied) fadingSlot.Player.SetCurrentWeight(fadingWeight);

                // 3. 子层 Mixer 相对归一化：保证 LayerMixer 内部输入权重和为 1.0（当有动画在播放时），输出 100% 满姿态插值，彻底杜绝 BindPose 污染
                float totalLayerWeight = activeRawWeight + fadingWeight;
                if (totalLayerWeight > 0.0001f)
                {
                    mixer.SetInputWeight(activeSlot.SlotIndex, activeRawWeight / totalLayerWeight);
                    mixer.SetInputWeight(fadingSlot.SlotIndex, fadingWeight / totalLayerWeight);
                }
                else
                {
                    mixer.SetInputWeight(activeSlot.SlotIndex, 0f);
                    mixer.SetInputWeight(fadingSlot.SlotIndex, 0f);
                }

                // 4. 更新 TopLevelMixer 中该 Layer 的总权重（控制该图层与底层 Locomotion 状态机的平滑淡入淡出）
                float cfgWeight = (layer.Config.LayerWeight > 0.0001f) ? layer.Config.LayerWeight : 1.0f;
                float effectiveLayerWeight = Mathf.Clamp01(totalLayerWeight) * Mathf.Clamp01(cfgWeight);
                _topLevelMixer.SetInputWeight(i + 1, effectiveLayerWeight);

                // 5. 释放已结束且权重归零的 Slot
                CheckAndReleaseSlot(layer.Slot0, mixer);
                CheckAndReleaseSlot(layer.Slot1, mixer);
            }
        }

        private void UpdateSlotPlayer(SlotState slot, float deltaTime)
        {
            if (!slot.IsOccupied)
            {
                slot.Weight = 0f;
                return;
            }

            var player = slot.Player;
            player.Tick(deltaTime);

            if (slot.SlotMixer.IsValid() && slot.SegmentPlayables.Count > 0)
            {
                player.EvaluateAnimationSegments(slot.TempEvalIndices, slot.TempEvalTimes, slot.TempEvalWeights);

                int count = slot.SegmentPlayables.Count;
                for (int i = 0; i < count; i++)
                {
                    slot.SlotMixer.SetInputWeight(i, 0.0f);
                }

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
                        slot.SlotMixer.SetInputWeight(segIdx, slot.TempEvalWeights[k]);
                    }
                }
            }
        }

        private void CheckAndReleaseSlot(SlotState slot, AnimationMixerPlayable mixer)
        {
            if (!slot.IsOccupied) return;

            var player = slot.Player;
            // 只有当播放器彻底标记为 Finished 且在混音器中的实际权重归零时，才安全销毁 Playable 并释放 Slot
            if (player.IsFinished && slot.Weight <= 0.0001f)
            {
                slot.DestroyPlayables(_playableGraph, mixer);
                UnbindPlayerEvents(player);
                slot.Reset();
            }
        }

        #endregion

        #region Root Motion 采样与解耦分发

        private void HandleAnimatorMove(Vector3 deltaPosition, Quaternion deltaRotation)
        {
            var layer0 = _layerStates.Count > 0 ? _layerStates[0] : null;
            if (layer0 == null) return;

            // 查找当前 Layer 0 中实际贡献权重的主导 Player
            SlotState activeSlot = (layer0.ActiveSlotIndex == 0) ? layer0.Slot0 : layer0.Slot1;
            SlotState fadingSlot = (layer0.ActiveSlotIndex == 0) ? layer0.Slot1 : layer0.Slot0;

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

                // 根据主导蒙太奇配置进行分量掩码过滤
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
                // 无活跃蒙太奇时，直接放行底层 Locomotion 的原生 Root Motion
                appliedDeltaPos = deltaPosition;
                appliedDeltaRot = deltaRotation;
            }

            // 1. 通过接口分发（供角色移动控制器按需实现）
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

            // 2. 通过 C# 事件委托广播分发（供外部移动组件按需订阅和二次限制过滤）
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

        /// <summary>
        /// 活跃播放器安全校验（要求未完成且代际完全匹配，用于受控修改指令与活跃播放状态查询）。
        /// </summary>
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

        /// <summary>
        /// 槽位播放器实例安全校验（只要槽位被占有且代际匹配即可，支持在结束结算时安全读取静态资产与时长元数据）。
        /// </summary>
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
            return TryGetValidPlayer(layerIndex, slotIndex, generation, out var player) ? player.CurrentWeight : 0f;
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
