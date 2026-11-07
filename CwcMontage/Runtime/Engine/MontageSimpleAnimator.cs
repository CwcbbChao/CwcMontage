using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 单个状态的蒙太奇动画资产绑定配置项（专属现代蒙太奇资产，基于 MontageSequenceAsset）。
    /// </summary>
    /// <typeparam name="TState">状态枚举类型（必须为枚举）</typeparam>
    [Serializable]
    public struct MontageStateBinding<TState> where TState : unmanaged, Enum
    {
        [Tooltip("绑定的状态枚举项")]
        public TState State;

        [Tooltip("绑定的现代蒙太奇动画资产（MontageSequenceAsset 容器）")]
        [UnityEngine.Serialization.FormerlySerializedAs("Asset")]
        public MontageSequenceAsset Montage;

        /// <summary>
        /// 解析当前绑定对应的有效 IMontageSequence 实例。
        /// </summary>
        public IMontageSequence ResolveSequence()
        {
            return Montage;
        }
    }

    /// <summary>
    /// 单个状态的基础动画切片绑定配置项（直接引用裸切片，作为轻量动作或保底表现）。
    /// 当对应状态未配置蒙太奇资产时，将在运行期纯内存极速组装。
    /// </summary>
    /// <typeparam name="TState">状态枚举类型（必须为枚举）</typeparam>
    [Serializable]
    public struct ClipStateBinding<TState> where TState : unmanaged, Enum
    {
        [Tooltip("绑定的状态枚举项")]
        public TState State;

        [Tooltip("绑定的基础动画切片（当对应状态未配置蒙太奇资产时，将在运行期纯内存极速组装）")]
        public AnimationClip Clip;

        [Tooltip("基础播放速率与时长缩放倍率（默认 1.0）。数值越大播放越快，有效解决固定动画时长与移速不匹配的问题")]
        [Min(0.01f)]
        public float PlayRate;

        /// <summary>
        /// 有效的播放速率倍率（若未设置或小于等于 0 则自动回退为 1.0）。
        /// </summary>
        public float EffectivePlayRate => PlayRate > 0.001f ? PlayRate : 1.0f;
    }

    /// <summary>
    /// 插件通用轻量蒙太奇状态动画控制器的非泛型抽象基类。
    /// 用于统一 Inspector 自定义编辑器绘制、反射识别与多态管理。
    /// </summary>
    public abstract class MontageSimpleAnimatorBase : MonoBehaviour
    {
        public abstract MontageController BaseController { get; }
        public abstract bool BaseHasActiveState { get; }
        public abstract string BaseCurrentStateName { get; }
        public abstract MontageSkeletonType BaseSkeletonType { get; set; }
        public abstract bool BaseIsHumanoid { get; }

        /// <summary>
        /// 获取所有状态枚举的名称列表（供编辑器绘制与调试）
        /// </summary>
        public abstract IReadOnlyList<string> BaseGetStateNames();

        /// <summary>
        /// 查询指定索引的状态是否配置了有效的蒙太奇资产或动画切片绑定
        /// </summary>
        public abstract bool BaseHasBindingForState(int stateIndex);

        /// <summary>
        /// 获取指定状态的默认优先级
        /// </summary>
        public abstract int BaseGetStateDefaultPriority(int stateIndex);

        /// <summary>
        /// 调试测试：触发带有时长的限时状态（按枚举自身数值作为唯一优先级）
        /// </summary>
        public abstract void BaseTriggerTimedState(int stateIndex, float duration);

        /// <summary>
        /// 调试测试：进入或退出持续状态（按枚举自身数值作为唯一优先级）
        /// </summary>
        public abstract void BaseSetSustainedState(int stateIndex, bool isActive);

        /// <summary>
        /// 调试测试：查询某个持续状态当前是否激活
        /// </summary>
        public abstract bool BaseIsSustainedStateActive(int stateIndex);

        /// <summary>
        /// 调试测试：停止并清除所有状态
        /// </summary>
        public abstract void BaseStopAllStates();
    }

    /// <summary>
    /// 插件通用轻量蒙太奇状态动画控制器基类（MontageSimpleAnimator）。
    /// 基于泛型枚举状态、独占式最高优先级仲裁，支持“声明持续状态”与“自适应限时触发”双输入模式。
    /// 原生支持直接传入零散 AnimationClip，运行期 0 GC 快速装配。
    /// 不耦合任何具体业务逻辑，作为 CwcMontage 插件的核心开箱即用套件。
    /// </summary>
    /// <typeparam name="TState">用户自定义的业务状态枚举类型</typeparam>
    public abstract class MontageSimpleAnimator<TState> : MontageSimpleAnimatorBase where TState : unmanaged, Enum
    {
        #region 1. 常量与类型定义

        protected const float DEFAULT_BLEND_TIME = 0.1f;
        protected static readonly string[] s_stateNames;
        protected static readonly TState[] s_stateValues;

        static MontageSimpleAnimator()
        {
            var fields = typeof(TState).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var namesList = new List<string>(fields.Length);
            var valuesList = new List<TState>(fields.Length);

            for (int i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                // 自动过滤被标记为 [Obsolete] 的废弃枚举项，避免别名污染编辑器与调试列表
                if (field.IsDefined(typeof(ObsoleteAttribute), false)) continue;

                namesList.Add(field.Name);
                valuesList.Add((TState)field.GetValue(null));
            }

            s_stateNames = namesList.ToArray();
            s_stateValues = valuesList.ToArray();
        }

        /// <summary>
        /// 持续状态运行时信息（如待机、行走）。
        /// </summary>
        protected struct SustainedStateInfo
        {
            public int Priority;
            public float DynamicParam;
        }

        /// <summary>
        /// 限时触发状态运行时信息（如受击、技能、攻击）。
        /// </summary>
        protected struct TimedStateInfo
        {
            public int Priority;
            public float ExpireTime;
            public float CalculatedSpeed;
            public Action OnComplete;
        }

        #endregion

        #region 2. Inspector 字段

        [HideInInspector]
        [SerializeField] private MontageSkeletonType _skeletonType = MontageSkeletonType.Humanoid;

        [Header("Montage Controller")]
        [Tooltip("绑定的底层 MontageController 核心调度器。若为空将在自身或子级自动查找")]
        [UnityEngine.Serialization.FormerlySerializedAs("_montageCoordinator")]
        [SerializeField] private MontageController _montageController;

        [Header("Montage Asset Bindings")]
        [Tooltip("状态与蒙太奇动画资产映射列表（优先级 1：优先匹配）")]
        [UnityEngine.Serialization.FormerlySerializedAs("_defaultBindings")]
        [SerializeField] private List<MontageStateBinding<TState>> _montageBindings = new List<MontageStateBinding<TState>>();

        [Header("Animation Clip Fallback Bindings")]
        [Tooltip("状态与基础动画切片映射列表（优先级 2：当未配置蒙太奇资产时作为轻量保底表现）")]
        [SerializeField] private List<ClipStateBinding<TState>> _clipBindings = new List<ClipStateBinding<TState>>();

        [Header("Debug Settings")]
        [Tooltip("是否开启状态机流转与到期调试日志（可在 Inspector 勾选测试）")]
        [SerializeField] private bool _enableDebugLogs = true;

        #endregion

        #region 3. 受保护运行时字段

        protected readonly Dictionary<TState, MontageStateBinding<TState>> _montageBindingDict = new Dictionary<TState, MontageStateBinding<TState>>();
        protected readonly Dictionary<TState, ClipStateBinding<TState>> _clipBindingDict = new Dictionary<TState, ClipStateBinding<TState>>();
        protected readonly Dictionary<TState, IMontageSequence> _runtimeResolvedBindings = new Dictionary<TState, IMontageSequence>();
        protected readonly Dictionary<(AnimationClip clip, int rateKey, bool isFullBody), IMontageSequence> _clipSequenceCache = new Dictionary<(AnimationClip clip, int rateKey, bool isFullBody), IMontageSequence>();
        protected readonly Dictionary<TState, SustainedStateInfo> _sustainedStates = new Dictionary<TState, SustainedStateInfo>();
        protected readonly Dictionary<TState, TimedStateInfo> _timedStates = new Dictionary<TState, TimedStateInfo>();

        // 缓存待清理的到期 Key 列表，避免 Update 中频繁分配产生 GC
        protected readonly List<TState> _expiredKeysBuffer = new List<TState>(4);

        protected bool _isInitialized = false;
        protected bool _hasCurrentState = false;
        protected TState _currentState;
        protected MontageHandle _currentActiveHandle = MontageHandle.Invalid;
        protected float _lastFailedLoopTriggerTime = -1f;

        #endregion

        #region 4. 公共属性与事件

        public override MontageController BaseController => _montageController;
        public override bool BaseHasActiveState => _hasCurrentState;
        public override string BaseCurrentStateName => _hasCurrentState ? _currentState.ToString() : "None";
        public override MontageSkeletonType BaseSkeletonType { get => SkeletonType; set => _skeletonType = value; }
        public override bool BaseIsHumanoid => IsHumanoid;

        public virtual IReadOnlyList<MontageStateBinding<TState>> MontageBindings => _montageBindings;
        public virtual IReadOnlyList<ClipStateBinding<TState>> ClipBindings => _clipBindings;

        public virtual MontageSkeletonType SkeletonType => _montageController != null ? _montageController.SkeletonType : MontageSkeletonType.Humanoid;
        public virtual bool IsHumanoid => _montageController == null || _montageController.IsHumanoid;

        public virtual MontageController Controller => _montageController;
        public virtual bool HasActiveState => _hasCurrentState;
        public virtual TState CurrentState => _currentState;
        public virtual MontageHandle ActiveHandle => _currentActiveHandle;
        public virtual bool EnableDebugLogs { get => _enableDebugLogs; set => _enableDebugLogs = value; }

        /// <summary>
        /// 约定移动动画的基准物理移速（米/秒，默认约定为 1.0m/s）。
        /// 外部直接传入真实物理移速，除以此基准后自动等比缩放播放速率，消除滑步。
        /// </summary>
        protected virtual float BaseLocomotionSpeed => 1.0f;

        /// <summary>
        /// 当某个状态开始播放蒙太奇时触发。
        /// </summary>
        public event Action<TState, MontageHandle> OnStateStarted;

        /// <summary>
        /// 当某个状态退出或被更高优先级打断时触发。
        /// </summary>
        public event Action<TState> OnStateEnded;

        #endregion

        #region 5. Unity 生命周期

        protected virtual void Awake()
        {
            Initialize();
        }

        protected virtual void Update()
        {
            UpdateTimedStatesExpiry();
            EnsureActiveStateLooping();
        }

        protected virtual void OnDisable()
        {
            StopAllStates();
        }

        #endregion

        #region 6. 公共控制 API

        /// <summary>
        /// 获取或为指定的零散动画切片纯内存快速组装只读蒙太奇数据（0 GC 享元缓存池）。
        /// </summary>
        /// <param name="clip">动画切片资产</param>
        /// <param name="playRate">基础播放速率倍率</param>
        /// <param name="isFullBody">是否为全身动画</param>
        public virtual IMontageSequence GetOrCreateSequenceForClip(AnimationClip clip, float playRate = 1.0f, bool isFullBody = true)
        {
            if (clip == null) return null;

            float safeRate = playRate > 0.001f ? playRate : 1.0f;
            int rateKey = Mathf.RoundToInt(safeRate * 1000f);
            var key = (clip, rateKey, isFullBody);

            if (!_clipSequenceCache.TryGetValue(key, out var seq))
            {
                seq = MontageSequenceData.CreateFromClip(clip, DEFAULT_BLEND_TIME, DEFAULT_BLEND_TIME, isFullBody, safeRate);
                _clipSequenceCache.Add(key, seq);
            }
            return seq;
        }

        /// <summary>
        /// 初始化组件与映射字典缓存。
        /// </summary>
        public virtual void Initialize()
        {
            if (_isInitialized) return;

            if (_montageController == null)
            {
                _montageController = GetComponent<MontageController>() ?? GetComponentInChildren<MontageController>(true);
            }

            _montageBindingDict.Clear();
            _clipBindingDict.Clear();
            _runtimeResolvedBindings.Clear();

            // 1. 注册蒙太奇资产映射（优先级 1）
            for (int i = 0; i < _montageBindings.Count; i++)
            {
                var binding = _montageBindings[i];
                if (!_montageBindingDict.ContainsKey(binding.State))
                {
                    _montageBindingDict.Add(binding.State, binding);
                    var resolved = binding.ResolveSequence();
                    if (resolved != null)
                    {
                        _runtimeResolvedBindings[binding.State] = resolved;
                    }
                }
            }

            // 2. 注册基础动画切片映射（优先级 2 / 保底表现，支持自定义基础播放速率）
            for (int i = 0; i < _clipBindings.Count; i++)
            {
                var clipBinding = _clipBindings[i];
                if (!_clipBindingDict.ContainsKey(clipBinding.State))
                {
                    _clipBindingDict.Add(clipBinding.State, clipBinding);
                    // 若对应状态未被蒙太奇资产占用且切片有效，提前组装保底享元加入缓存
                    if (!_runtimeResolvedBindings.ContainsKey(clipBinding.State) && clipBinding.Clip != null)
                    {
                        var resolved = GetOrCreateSequenceForClip(clipBinding.Clip, clipBinding.EffectivePlayRate, true);
                        if (resolved != null)
                        {
                            _runtimeResolvedBindings[clipBinding.State] = resolved;
                        }
                    }
                }
            }

            _isInitialized = true;
        }

        /// <summary>
        /// 外部动态替换/覆写某个状态的默认蒙太奇配置（现代 MontageSequenceAsset 资产）。
        /// </summary>
        public virtual void OverrideStateMontage(TState state, MontageSequenceAsset newMontage)
        {
            OverrideStateSequence(state, newMontage);
        }

        /// <summary>
        /// 外部动态替换/覆写某个状态的默认序列配置（支持新版 Asset 或纯 C# 数据）。
        /// </summary>
        public virtual void OverrideStateSequence(TState state, IMontageSequence newSequence)
        {
            if (!_isInitialized)
            {
                Initialize();
            }

            _runtimeResolvedBindings[state] = newSequence;

            // 如果当前正好在播这个状态，立即重新仲裁刷新表现
            if (_hasCurrentState && EqualityComparer<TState>.Default.Equals(_currentState, state))
            {
                EvaluateActiveStates();
            }
        }

        /// <summary>
        /// 外部动态使用零散动画切片覆写指定状态（运行期 0 Native SO 创建）。
        /// </summary>
        public virtual void OverrideStateClip(TState state, AnimationClip newClip, float playRate = 1.0f, bool isFullBody = true)
        {
            IMontageSequence seq = GetOrCreateSequenceForClip(newClip, playRate, isFullBody);
            OverrideStateSequence(state, seq);
        }

        /// <summary>
        /// 获取状态枚举自带的唯一数值作为仲裁优先级。
        /// </summary>
        public virtual int GetStatePriority(TState state)
        {
            return Convert.ToInt32(state);
        }

        /// <summary>
        /// 模式一：声明持续状态（如待机、走跑、浮空，按枚举自带数值作为唯一优先级）。
        /// 持续状态由外部逻辑系统显式维护生命周期，支持在保持激活的同时动态更新物理速度等参数以消除滑步。
        /// </summary>
        /// <param name="state">状态枚举</param>
        /// <param name="isActive">是否处于激活状态</param>
        /// <param name="dynamicParam">动态参数（如实时移速）</param>
        public virtual void SetSustainedState(TState state, bool isActive, float dynamicParam = 1.0f)
        {
            SetSustainedStateInternal(state, isActive, dynamicParam);
        }

        /// <summary>
        /// 模式二：触发限时瞬态状态（如受击、技能、普攻，按枚举自带数值作为唯一优先级）。
        /// 激活模型在当前状态预设配置的有效蒙太奇；
        /// 系统自动调整播放速率使动作严格匹配声明时长（Time-Warping），到期后自毁并自动平滑切回次高优先级状态。
        /// </summary>
        /// <param name="state">状态枚举</param>
        /// <param name="duration">期望持续时间（秒）。若小于等于 0，则自动按蒙太奇自身的物理时长播放</param>
        /// <param name="onComplete">该状态到期或播完时的回调</param>
        public virtual void TriggerTimedState(TState state, float duration, Action onComplete = null)
        {
            TriggerTimedStateInternal(state, duration, onComplete);
        }

        /// <summary>
        /// 模式二扩展：按动作自身物理时长触发限时状态。
        /// </summary>
        public virtual void TriggerTimedState(TState state, Action onComplete = null)
        {
            TriggerTimedState(state, 0f, onComplete);
        }

        /// <summary>
        /// 主动打断或提前移除指定的限时状态。
        /// </summary>
        /// <param name="state">状态枚举</param>
        public virtual void RemoveTimedState(TState state)
        {
            if (_enableDebugLogs)
            {
                Debug.Log($"[MontageAnimator][{gameObject.name}] 收到 RemoveTimedState({state})，Frame: {Time.frameCount}");
            }

            if (_timedStates.TryGetValue(state, out var info))
            {
                _timedStates.Remove(state);
                info.OnComplete?.Invoke();
                EvaluateActiveStates();
            }
        }

        /// <summary>
        /// 停止所有状态池与当前播放的动画，完全重置。
        /// </summary>
        public virtual void StopAllStates(float blendOutDuration = DEFAULT_BLEND_TIME)
        {
            if (_enableDebugLogs)
            {
                Debug.Log($"[MontageAnimator][{gameObject.name}] 收到 StopAllStates，当前状态: {_currentState}，活跃限时状态数: {_timedStates.Count}，Frame: {Time.frameCount}");
            }

            // 安全通知所有未完成的限时回调，防止外部业务逻辑死锁
            foreach (var kvp in _timedStates)
            {
                kvp.Value.OnComplete?.Invoke();
            }

            _sustainedStates.Clear();
            _timedStates.Clear();
            _expiredKeysBuffer.Clear();

            if (_hasCurrentState)
            {
                TState previousState = _currentState;
                StopCurrentActiveHandle(blendOutDuration);
                _hasCurrentState = false;
                _currentState = default;
                OnStateSwitched(previousState, default);
                OnStateEnded?.Invoke(previousState);
            }
        }

        #region 6.1 编辑器与调试测试接口实现

        public override IReadOnlyList<string> BaseGetStateNames() => s_stateNames;

        public override bool BaseHasBindingForState(int stateIndex)
        {
            if (stateIndex < 0 || stateIndex >= s_stateValues.Length) return false;
            TState state = s_stateValues[stateIndex];
            return _runtimeResolvedBindings.ContainsKey(state)
                || _montageBindingDict.ContainsKey(state)
                || _clipBindingDict.ContainsKey(state);
        }

        public override int BaseGetStateDefaultPriority(int stateIndex)
        {
            if (stateIndex < 0 || stateIndex >= s_stateValues.Length) return 100;
            try
            {
                int val = Convert.ToInt32(s_stateValues[stateIndex]);
                return val != 0 ? val : 100;
            }
            catch
            {
                return 100;
            }
        }

        public override void BaseTriggerTimedState(int stateIndex, float duration)
        {
            if (stateIndex < 0 || stateIndex >= s_stateValues.Length) return;
            TState state = s_stateValues[stateIndex];
            TriggerTimedState(state, duration);
        }

        public override void BaseSetSustainedState(int stateIndex, bool isActive)
        {
            if (stateIndex < 0 || stateIndex >= s_stateValues.Length) return;
            TState state = s_stateValues[stateIndex];
            SetSustainedState(state, isActive, 1.0f);
        }

        public override bool BaseIsSustainedStateActive(int stateIndex)
        {
            if (stateIndex < 0 || stateIndex >= s_stateValues.Length) return false;
            TState state = s_stateValues[stateIndex];
            return _sustainedStates.ContainsKey(state);
        }

        [ContextMenu("Debug: Stop All States")]
        public override void BaseStopAllStates()
        {
            StopAllStates();
        }

        #endregion

        #endregion

        #region 7. 受保护核心仲裁与调度

        /// <summary>
        /// 快速判定指定的蒙太奇序列是否与当前模型骨骼匹配（契约式校验）。
        /// </summary>
        public virtual bool IsMontageCompatible(IMontageSequence montage)
        {
            if (montage == null) return false;
            return _montageController == null || _montageController.IsMontageCompatible(montage);
        }

        /// <summary>
        /// 解析指定状态在当前模型上配置绑定的有效蒙太奇序列契约实例（纯粹基于自身配置映射）。
        /// 优先级：蒙太奇资产绑定 (_montageBindings) -> 裸动画切片绑定 (_clipBindings) -> 运行时动态覆写。
        /// </summary>
        public virtual IMontageSequence ResolveTargetSequence(TState state)
        {
            // 1. 查找模型配置的蒙太奇动画资产（优先级 1：优先命中）
            if (_montageBindingDict.TryGetValue(state, out var montageBinding))
            {
                var seq = montageBinding.ResolveSequence();
                if (seq != null)
                {
                    return seq;
                }
            }

            // 2. 查找模型配置的基础动画切片引用（优先级 2：保底切片，0 GC 享元组装）
            if (_clipBindingDict.TryGetValue(state, out var clipBinding))
            {
                if (clipBinding.Clip != null)
                {
                    return GetOrCreateSequenceForClip(clipBinding.Clip, clipBinding.EffectivePlayRate, true);
                }
            }

            // 3. 查找动态覆写配置
            if (_runtimeResolvedBindings.TryGetValue(state, out var resolved) && resolved != null)
            {
                return resolved;
            }

            return null;
        }

        /// <summary>
        /// 依据配置映射解析出最终生效的现代蒙太奇资产实例（MontageSequenceAsset）。
        /// </summary>
        public virtual MontageSequenceAsset ResolveTargetAsset(TState state)
        {
            return ResolveTargetSequence(state) as MontageSequenceAsset;
        }

        protected virtual void SetSustainedStateInternal(TState state, bool isActive, float dynamicParam)
        {
            if (isActive)
            {
                _sustainedStates[state] = new SustainedStateInfo
                {
                    Priority = GetStatePriority(state),
                    DynamicParam = dynamicParam
                };
            }
            else
            {
                _sustainedStates.Remove(state);
            }

            EvaluateActiveStates();
        }

        /// <summary>
        /// 限时状态内部装载（纯粹基于自身配置，严格根据外部声明时长执行速度缩放 Time-Warping）。
        /// 原生支持同名限时状态重复触发打断重播（Re-trigger）。
        /// </summary>
        protected virtual void TriggerTimedStateInternal(TState state, float duration, Action onComplete)
        {
            IMontageSequence targetSequence = ResolveTargetSequence(state);

            if (_enableDebugLogs)
            {
                Debug.Log($"[MontageAnimator][{gameObject.name}] TriggerTimedStateInternal: {state}, 传入duration: {duration}, " +
                          $"序列解析成功: {targetSequence != null}, 原始时长: {targetSequence?.TotalDuration ?? 0f}, Frame: {Time.frameCount}");
            }

            if (targetSequence == null)
            {
                if (_enableDebugLogs)
                {
                    Debug.LogWarning($"[MontageAnimator][{gameObject.name}] 状态 '{state}' 未配置有效蒙太奇序列或切片绑定，立即触发 OnComplete！Frame: {Time.frameCount}");
                }
                onComplete?.Invoke();
                return;
            }

            // 若同一状态槽位已有正在等待的回调，先安全执行旧回调防止业务死锁
            if (_timedStates.TryGetValue(state, out var oldInfo))
            {
                if (_enableDebugLogs)
                {
                    Debug.Log($"[MontageAnimator][{gameObject.name}] 状态 '{state}' 重复触发打断，先执行旧回调。Frame: {Time.frameCount}");
                }
                oldInfo.OnComplete?.Invoke();
            }

            // 速度缩放：严格让动画切片的总播放长度与外部能力期望的 duration 吻合
            float rawDuration = targetSequence.TotalDuration;
            float actualDuration = duration > 0.001f ? duration : Mathf.Max(rawDuration, 0.05f);
            float calculatedSpeed = (duration > 0.001f && rawDuration > 0.001f) ? (rawDuration / duration) : 1.0f;

            _timedStates[state] = new TimedStateInfo
            {
                Priority = GetStatePriority(state),
                ExpireTime = Time.time + actualDuration,
                CalculatedSpeed = calculatedSpeed,
                OnComplete = onComplete
            };

            // 显式传入 forceRetrigger: true，告知仲裁器若该限时状态在竞争中胜出，必须重头播放（Re-trigger）
            EvaluateActiveStates(forceRetrigger: true);
        }

        /// <summary>
        /// 独占式优先级核心仲裁：扫描所有活跃池，选出最高优先级项执行独占播放。
        /// </summary>
        /// <param name="forceRetrigger">是否强制重播（当同名限时状态再次被显式触发时执行重头播放）</param>
        protected virtual void EvaluateActiveStates(bool forceRetrigger = false)
        {
            bool found = false;
            TState bestState = default;
            int highestPriority = int.MinValue;
            bool isTimed = false;
            float bestSpeed = 1.0f;
            IMontageSequence bestSequence = null;

            // 1. 扫描持续状态池
            foreach (var kvp in _sustainedStates)
            {
                IMontageSequence seq = ResolveTargetSequence(kvp.Key);
                if (seq != null)
                {
                    if (kvp.Value.Priority > highestPriority)
                    {
                        highestPriority = kvp.Value.Priority;
                        bestState = kvp.Key;
                        bestSequence = seq;
                        found = true;
                        isTimed = false;

                        // 约定基准移速：外部直接传入真实物理移速，除以基准速度后自动缩放步频消除滑步
                        float baseSpeed = kvp.Value.DynamicParam > 0.001f ? (kvp.Value.DynamicParam / BaseLocomotionSpeed) : 1.0f;
                        bestSpeed = baseSpeed;
                    }
                }
            }

            // 2. 扫描限时触发状态池（优先级相同时优先于持续状态）
            foreach (var kvp in _timedStates)
            {
                if (kvp.Value.Priority >= highestPriority)
                {
                    IMontageSequence sequence = ResolveTargetSequence(kvp.Key);
                    if (sequence != null)
                    {
                        highestPriority = kvp.Value.Priority;
                        bestState = kvp.Key;
                        bestSequence = sequence;
                        found = true;
                        isTimed = true;
                        bestSpeed = kvp.Value.CalculatedSpeed;
                    }
                }
            }

            // 3. 执行仲裁表现
            if (!found)
            {
                if (_hasCurrentState)
                {
                    TState previousState = _currentState;
                    StopCurrentActiveHandle(DEFAULT_BLEND_TIME);
                    _hasCurrentState = false;
                    _currentState = default;
                    OnStateSwitched(previousState, default);
                    OnStateEnded?.Invoke(previousState);
                }
                return;
            }

            bool stateChanged = !_hasCurrentState || !EqualityComparer<TState>.Default.Equals(bestState, _currentState);
            bool shouldRetrigger = isTimed && forceRetrigger && EqualityComparer<TState>.Default.Equals(bestState, _currentState);

            if (stateChanged || shouldRetrigger)
            {
                ExecuteSwitchToState(bestState, bestSequence, bestSpeed);
            }
            else
            {
                // 相同持续状态无缝更新播放速率（消除滑步）
                if (!isTimed && _currentActiveHandle.IsValid)
                {
                    _currentActiveHandle.SetPlaybackRate(bestSpeed);
                }
            }
        }

        /// <summary>
        /// 执行切换到目标状态蒙太奇播放。
        /// </summary>
        protected virtual void ExecuteSwitchToState(TState targetState, IMontageSequence montage, float speed)
        {
            if (montage == null || _montageController == null) return;

            if (_hasCurrentState)
            {
                OnStateEnded?.Invoke(_currentState);
            }

            TState oldState = _currentState;
            _currentState = targetState;
            _hasCurrentState = true;
            _lastFailedLoopTriggerTime = -1f;

            _currentActiveHandle = _montageController.Play(montage);
            if (_currentActiveHandle.IsValid && Mathf.Abs(speed - 1.0f) > 0.001f)
            {
                _currentActiveHandle.SetPlaybackRate(speed);
            }

            if (_enableDebugLogs)
            {
                Debug.Log($"[MontageAnimator][{gameObject.name}] SwitchToState: {oldState} -> {targetState}, " +
                          $"HandleValid: {_currentActiveHandle.IsValid}, IsPlaying: {_currentActiveHandle.IsPlaying}, " +
                          $"TotalDuration: {_currentActiveHandle.TotalDuration}, Frame: {Time.frameCount}");
            }

            OnStateSwitched(oldState, targetState);
            OnStateStarted?.Invoke(targetState, _currentActiveHandle);
        }

        /// <summary>
        /// 当状态发生实质切换时的扩展虚方法，供业务子类重写对接（如联动 MMFeedbacks、特效或音效）。
        /// </summary>
        /// <param name="previousState">切换前的状态</param>
        /// <param name="newState">切换后的新状态</param>
        protected virtual void OnStateSwitched(TState previousState, TState newState)
        {
        }

        /// <summary>
        /// 轮询限时状态生命周期，到期或底层动画自然播完自动剔除并触发回调。
        /// 遵循“以控制器句柄为单一权威源”并在“物理动作最后一帧精准接力”的公理：
        /// 当动画到达末尾（RemainingTime 小于等于最后一帧阈值或 IsFinished）时，在当前帧立即派发完成通知，
        /// 彻底杜绝下一帧才响应导致的 1 帧漏底闪烁。
        /// </summary>
        protected virtual void UpdateTimedStatesExpiry()
        {
            if (_timedStates.Count == 0) return;

            _expiredKeysBuffer.Clear();
            float now = Time.time;
            float frameThreshold = Mathf.Max(Time.deltaTime * 1.5f, 0.02f);

            foreach (var kvp in _timedStates)
            {
                bool isExpired = false;

                // 1. 若当前活跃状态正是该限时状态，以底层句柄为唯一权威时钟
                if (_hasCurrentState && EqualityComparer<TState>.Default.Equals(_currentState, kvp.Key))
                {
                    bool isHandleInvalid = !_currentActiveHandle.IsValid;
                    bool isHandleFinished = _currentActiveHandle.IsFinished;
                    bool isRemainingLow = _currentActiveHandle.RemainingTime <= frameThreshold;

                    if (isHandleInvalid || isHandleFinished || isRemainingLow)
                    {
                        isExpired = true;
                        if (_enableDebugLogs)
                        {
                            Debug.LogWarning($"[MontageAnimator][{gameObject.name}] 活跃限时状态 '{kvp.Key}' 判定到期！" +
                                             $"原因: HandleInvalid={isHandleInvalid}, Finished={isHandleFinished}, RemainingLow={isRemainingLow} " +
                                             $"(Remaining={_currentActiveHandle.RemainingTime:F3}s, Threshold={frameThreshold:F3}s), Frame: {Time.frameCount}");
                        }
                    }
                }
                else
                {
                    // 2. 若是非当前活跃状态（在后台等待），以设定的超时时间为基准，并在最后一帧提前完成
                    if (now + frameThreshold >= kvp.Value.ExpireTime)
                    {
                        isExpired = true;
                        if (_enableDebugLogs)
                        {
                            Debug.LogWarning($"[MontageAnimator][{gameObject.name}] 后台限时状态 '{kvp.Key}' 设定时长已到期！Frame: {Time.frameCount}");
                        }
                    }
                }

                if (isExpired)
                {
                    _expiredKeysBuffer.Add(kvp.Key);
                }
            }

            if (_expiredKeysBuffer.Count > 0)
            {
                for (int i = 0; i < _expiredKeysBuffer.Count; i++)
                {
                    var key = _expiredKeysBuffer[i];
                    if (_timedStates.TryGetValue(key, out var info))
                    {
                        _timedStates.Remove(key);
                        if (_enableDebugLogs)
                        {
                            Debug.Log($"[MontageAnimator][{gameObject.name}] 状态 '{key}' 出队列，触发 OnComplete 回调... Frame: {Time.frameCount}");
                        }
                        info.OnComplete?.Invoke();
                    }
                }

                // 到期自毁后立即重新仲裁，自动平滑切回次高优先级状态
                EvaluateActiveStates();
            }
        }

        /// <summary>
        /// 停止当前正在播放的蒙太奇句柄。
        /// </summary>
        protected virtual void StopCurrentActiveHandle(float blendOutTime)
        {
            if (_currentActiveHandle.IsValid)
            {
                _currentActiveHandle.Stop(blendOutTime);
                _currentActiveHandle = MontageHandle.Invalid;
            }
        }

        /// <summary>
        /// 检查当前持续状态的循环保活：
        /// 严格限定：只有持续状态（Sustained State，如待机、走跑、浮空、死亡常驻）才需要循环保活，限时瞬态绝不循环；
        /// 遵循“以切片为原子单元”的跨播放姿态交接公理：
        /// 当动画播满全部物理帧（或接近末尾）时，由外部自然发起下一轮播放（Play）；
        /// 依赖 MontageController 统一的双 Slot 跨切片平滑交接机制，上一轮的末尾姿态自动锁定定格（Pose Hold）并平滑让渡权重；
        /// 杜绝在同一 Slot 内部硬拉时间轴造成的姿态丢失与底层淡入闪烁。
        /// </summary>
        protected virtual void EnsureActiveStateLooping()
        {
            if (!_hasCurrentState || _montageController == null) return;

            // 严格限定：只有持续状态（Sustained State）才需要循环保活，瞬态单次执行绝不循环
            if (!_sustainedStates.TryGetValue(_currentState, out var sustainedInfo))
            {
                return;
            }

            IMontageSequence targetSequence = ResolveTargetSequence(_currentState);
            if (targetSequence == null) return;

            float currentSpeed = sustainedInfo.DynamicParam > 0.001f 
                ? (sustainedInfo.DynamicParam / BaseLocomotionSpeed) 
                : 1.0f;

            bool needLoopTrigger = false;
            if (_currentActiveHandle.IsValid && _currentActiveHandle.IsPlaying)
            {
                float threshold = Mathf.Max(Time.deltaTime * currentSpeed * 1.5f, 0.02f);
                if (_currentActiveHandle.RemainingTime <= threshold)
                {
                    needLoopTrigger = true;
                }
            }
            else if (!_currentActiveHandle.IsValid || _currentActiveHandle.IsFinished)
            {
                if (_lastFailedLoopTriggerTime < 0f || Time.time - _lastFailedLoopTriggerTime >= 0.25f)
                {
                    needLoopTrigger = true;
                }
            }

            if (needLoopTrigger)
            {
                _currentActiveHandle = _montageController.Play(targetSequence);
                if (_currentActiveHandle.IsValid)
                {
                    _lastFailedLoopTriggerTime = -1f;
                    if (Mathf.Abs(currentSpeed - 1.0f) > 0.001f)
                    {
                        _currentActiveHandle.SetPlaybackRate(currentSpeed);
                    }
                }
                else
                {
                    _lastFailedLoopTriggerTime = Time.time;
                }
            }
        }

        #endregion
    }
}
