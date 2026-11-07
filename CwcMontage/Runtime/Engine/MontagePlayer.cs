using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// Runtime lifecycle state of the montage player.
    /// </summary>
    public enum MontagePlayerState
    {
        /// <summary>
        /// Playing normally (covers fade-in, steady playback, and the natural tail fade-out).
        /// </summary>
        Playing,

        /// <summary>
        /// Received an external interrupt request and is performing a smooth fade-out.
        /// </summary>
        Stopping,

        /// <summary>
        /// Playback is fully complete (finished naturally or faded to zero).
        /// </summary>
        Finished
    }

    /// <summary>
    /// Pure C# runtime montage player.
    /// Maintains playback time advancement, smooth weight computation, interval sweep event evaluation, and action block lifecycle.
    /// </summary>
    public class MontagePlayer
    {
        #region Protected & Private Fields

        protected IMontageSequence _sourceSequence;
        protected readonly GameObject _targetObject;
        protected readonly Animator _targetAnimator;
        protected readonly MontageController _controller;
        protected readonly bool _isPreview;

        protected IMontageBlockState[] _states = new IMontageBlockState[8];
        protected readonly HashSet<int> _activeBlockIndices = new(8);
        protected int _configuredBlockCount;

        protected readonly Dictionary<int, float> _sectionRates = new();

        protected MontagePlayerState _state = MontagePlayerState.Playing;
        protected float _elapsedTime;
        protected float _lastElapsedTime;
        protected float _playbackRate = 1.0f;
        protected float _currentWeight;
        protected float _customWeightMultiplier = 1.0f;

        // 外部主动打断退出专用平滑时钟
        protected float _interruptBlendOutTime = 0.1f;
        protected float _currentInterruptTime;
        protected float _weightAtInterrupt = 1.0f;

        protected int _currentSectionIndex;

        protected bool _isPaused;
        protected bool _isLogicalFinished;

        #endregion

        #region Properties

        /// <summary>
        /// Associated source montage configuration sequence (Contract-based).
        /// </summary>
        public IMontageSequence Sequence => _sourceSequence;

        /// <summary>
        /// Backward-compatible legacy SO reference (returns null if playing dynamic pure C# data).
        /// </summary>
        public MontageSequenceSO SourceAsset => _sourceSequence as MontageSequenceSO;

        /// <summary>
        /// Target host GameObject.
        /// </summary>
        public GameObject TargetObject => _targetObject;

        /// <summary>
        /// Target Animator.
        /// </summary>
        public Animator TargetAnimator => _targetAnimator;

        /// <summary>
        /// Bound controller component.
        /// </summary>
        public MontageController Controller => _controller;

        /// <summary>
        /// Backward-compatibility alias for Controller.
        /// </summary>
        [Obsolete("Use Controller instead.")]
        public MontageController Coordinator => _controller;

        /// <summary>
        /// Current lifecycle state of the player.
        /// </summary>
        public MontagePlayerState State => _state;

        /// <summary>
        /// Absolute timestamp of the current playback progress (seconds).
        /// </summary>
        public float ElapsedTime => _elapsedTime;

        /// <summary>
        /// Absolute timestamp of the previous sweep (seconds).
        /// </summary>
        public float LastElapsedTime => _lastElapsedTime;

        /// <summary>
        /// Absolute timestamp used by the underlying Playable for bone sampling (seconds; automatically clamps to the clip duration).
        /// </summary>
        public float ClipSampleTime
        {
            get
            {
                if (_sourceSequence == null || TotalDuration <= 0.0001f) return 0f;
                return Mathf.Min(_elapsedTime, TotalDuration);
            }
        }

        /// <summary>
        /// Current playback rate multiplier.
        /// </summary>
        public float PlaybackRate => _playbackRate;

        /// <summary>
        /// Current computed weight of this animation in the mixer [0.0, 1.0].
        /// </summary>
        public float CurrentWeight => _currentWeight;

        /// <summary>
        /// External custom playback weight multiplier [0.0, 1.0].
        /// </summary>
        public float CustomWeight
        {
            get => _customWeightMultiplier;
            set => _customWeightMultiplier = Mathf.Clamp01(value);
        }

        /// <summary>
        /// Index of the current physical section.
        /// </summary>
        public int CurrentSectionIndex => _currentSectionIndex;

        /// <summary>
        /// Total number of physical sections in the current montage sequence. Returns 0 if no sequence is assigned.
        /// </summary>
        public virtual int SectionCount => _sourceSequence != null ? _sourceSequence.SectionCount : 0;

        /// <summary>
        /// Whether the current montage sequence has multiple physical sections (SectionCount > 1).
        /// </summary>
        public virtual bool HasSections => SectionCount > 1;

        /// <summary>
        /// Queries whether the current montage sequence contains the specified physical section.
        /// </summary>
        /// <param name="sectionIndex">Physical section index.</param>
        public virtual bool HasSection(int sectionIndex) => _sourceSequence != null && sectionIndex >= 0 && sectionIndex < _sourceSequence.SectionCount;

        /// <summary>
        /// Whether the player is playing normally.
        /// </summary>
        public bool IsPlaying => _state == MontagePlayerState.Playing;

        /// <summary>
        /// Whether the player is stopping via an interrupt fade-out.
        /// </summary>
        public bool IsStopping => _state == MontagePlayerState.Stopping;

        /// <summary>
        /// Whether the montage has reached its authoritative total duration and logically completed.
        /// </summary>
        public bool IsLogicalFinished => _isLogicalFinished;

        /// <summary>
        /// Whether playback is fully complete (either logically finished its physical action or completely stopped).
        /// </summary>
        public bool IsFinished => _state == MontagePlayerState.Finished || _isLogicalFinished;

        /// <summary>
        /// Whether the player is stopped (fading out or completely finished).
        /// </summary>
        public bool IsStopped => _state == MontagePlayerState.Stopping || _state == MontagePlayerState.Finished;

        /// <summary>
        /// Whether the player is paused.
        /// </summary>
        public bool IsPaused => _isPaused;

        /// <summary>
        /// Duration of the current core animation clip (seconds).
        /// </summary>
        public float TotalDuration => _sourceSequence != null ? _sourceSequence.TotalDuration : 0f;

        /// <summary>
        /// Natural end of a single playback of the current animation (seconds; includes all segment post blend-outs).
        /// </summary>
        public float NaturalEndTime => _sourceSequence != null ? _sourceSequence.NaturalEndTime : 0f;

        #endregion

        #region Events

        /// <summary>
        /// Raised when the animation finishes playing naturally.
        /// </summary>
        public event Action<MontagePlayer> OnFinished;

        /// <summary>
        /// Raised when playback is stopped by an external interrupt.
        /// </summary>
        public event Action<MontagePlayer> OnInterrupted;

        /// <summary>
        /// Raised when the timeline crosses a physical section split point (argument is the new section index).
        /// </summary>
        public event Action<MontagePlayer, int> OnSectionEntered;

        #endregion

        #region Constructors

        /// <summary>
        /// Lightweight initialization of a pooled player instance (zero heap allocation; kept resident for micro-pool reuse).
        /// </summary>
        public MontagePlayer(
            GameObject targetObject,
            Animator targetAnimator,
            bool isPreview = false,
            MontageController controller = null)
        {
            _targetObject = targetObject;
            _targetAnimator = targetAnimator;
            _isPreview = isPreview;
            _controller = controller;
            _state = MontagePlayerState.Finished;
        }

        /// <summary>
        /// Modern constructor accepting any IMontageSequence (pure C# data, new asset or legacy SO).
        /// </summary>
        public MontagePlayer(
            IMontageSequence sourceSequence,
            GameObject targetObject,
            Animator targetAnimator,
            float? customBlendInTime = null,
            AnimationCurve customBlendInCurve = null,
            bool isPreview = false,
            MontageController controller = null)
            : this(targetObject, targetAnimator, isPreview, controller)
        {
            if (sourceSequence != null)
            {
                Play(sourceSequence, customBlendInTime, customBlendInCurve);
            }
        }

        /// <summary>
        /// Backward-compatible constructor (creates the player and starts playback in place immediately).
        /// </summary>
        public MontagePlayer(
            MontageSequenceSO sourceAsset,
            GameObject targetObject,
            Animator targetAnimator,
            float? customBlendInTime = null,
            AnimationCurve customBlendInCurve = null,
            bool isPreview = false,
            MontageController controller = null)
            : this((IMontageSequence)sourceAsset, targetObject, targetAnimator, customBlendInTime, customBlendInCurve, isPreview, controller)
        {
        }

        #endregion

        #region Reset & Initialization API

        /// <summary>
        /// Backward-compatible overload accepting legacy MontageSequenceSO.
        /// </summary>
        public virtual void Play(
            MontageSequenceSO asset,
            float? customBlendInTime = null,
            AnimationCurve customBlendInCurve = null)
        {
            Play((IMontageSequence)asset, customBlendInTime, customBlendInCurve);
        }

        /// <summary>
        /// Starts or restarts playback of the specified montage configuration in place (0 GC alloc).
        /// Supports pure C# data (MontageSequenceData), new assets, or legacy SO.
        /// </summary>
        public virtual void Play(
            IMontageSequence sequence,
            float? customBlendInTime = null,
            AnimationCurve customBlendInCurve = null)
        {
            if (sequence == null)
            {
                Debug.LogError("[MontagePlayer] Cannot start playback: sequence is null.");
                return;
            }

            // 1. If any active states were not returned yet, reset and return them safely first
            ResetStates();

            _sourceSequence = sequence;
            _state = MontagePlayerState.Playing;
            _isPaused = false;
            _isLogicalFinished = false;

            _elapsedTime = 0f;
            _lastElapsedTime = -0.0001f;
            _playbackRate = 1.0f;

            _interruptBlendOutTime = 0.1f;
            _currentInterruptTime = 0f;
            _weightAtInterrupt = 1.0f;
            _currentWeight = 1.0f;
            _currentSectionIndex = 0;
            _sectionRates.Clear();

            // 2. Get the baked flat read-only runtime block list (eliminates cloning entirely)
            var blocks = _sourceSequence.BakedRuntimeActionBlocks;
            int requiredBlocks = blocks != null ? blocks.Count : 0;
            _configuredBlockCount = requiredBlocks;

            // 3. Grow the state array adaptively
            if (requiredBlocks > _states.Length)
            {
                int newCap = Mathf.Max(requiredBlocks, _states.Length * 2);
                Array.Resize(ref _states, newCap);
            }

            // 4. Get state instances from the per-type pool (0-GC lifetime reuse, no delegate allocations)
            for (int i = 0; i < _configuredBlockCount; i++)
            {
                var action = blocks[i].Action;
                if (action?.StateType != null)
                {
                    var state = MontageBlockStatePool.Get(action.StateType);
                    _states[i] = state ?? action.CreateState();
                }
                else
                {
                    _states[i] = null;
                }
            }
        }

        protected virtual void ResetStates()
        {
            if (_sourceSequence == null) return;
            var blocks = _sourceSequence.BakedRuntimeActionBlocks;
            var context = CreateCurrentContext();

            ExitAllActiveBlocks(context);

            for (int i = 0; i < _configuredBlockCount; i++)
            {
                if (_states[i] != null && blocks != null && i < blocks.Count && blocks[i].Action?.StateType != null)
                {
                    MontageBlockStatePool.Release(blocks[i].Action.StateType, _states[i]);
                }
                _states[i] = null;
            }

            _activeBlockIndices.Clear();
            _configuredBlockCount = 0;
        }

        #endregion

        #region Core Update Logic (Tick & Sweep)

        /// <summary>
        /// Per-frame update called by MontageController or the editor UI from Update.
        /// Drives authoritative timeline stepping, fade in/out computation, section switching, and atomic interval sweep lifecycle dispatch.
        /// </summary>
        /// <param name="deltaTime">Time step (seconds)</param>
        protected internal virtual void Tick(float deltaTime)
        {
            if (_state == MontagePlayerState.Finished || _isPaused)
            {
                return;
            }

            // 1. 外部打断退出淡出阶段：推进打断淡出时钟，衰减完毕后标记彻底结束
            if (_state == MontagePlayerState.Stopping)
            {
                _currentInterruptTime = Mathf.Min(_currentInterruptTime + deltaTime, _interruptBlendOutTime);
                if (_currentInterruptTime >= _interruptBlendOutTime || CalculateTargetWeight() <= 0.0001f)
                {
                    _state = MontagePlayerState.Finished;
                }
                return;
            }

            // 2. 正常播放：推进 Authoritative 时间轴
            int prevSection = _currentSectionIndex;
            float effectiveDelta = deltaTime * _playbackRate;

            float naturalEnd = _sourceSequence.NaturalEndTime;
            float totalDuration = _sourceSequence.TotalDuration;
            float nextTime = _elapsedTime + effectiveDelta;

            // 阶段 A：物理动作预期完成点（到达 TotalDuration 最后一帧，派发逻辑完成回调）
            if (!_isLogicalFinished && nextTime >= totalDuration)
            {
                _isLogicalFinished = true;
                _elapsedTime = totalDuration;
                float sweepTo = totalDuration;
                float sweepFrom = Mathf.Min(_lastElapsedTime, totalDuration);
                if (sweepTo > sweepFrom || _activeBlockIndices.Count > 0)
                {
                    SweepInterval(sweepFrom, sweepTo, effectiveDelta);
                }
                _lastElapsedTime = _elapsedTime;

                int finalSection = _sourceSequence.GetSectionIndexAtTime(_elapsedTime);
                if (finalSection != prevSection)
                {
                    HandleSectionTransitions(prevSection, finalSection);
                    prevSection = finalSection;
                }

                var endContext = CreateCurrentContext();
                ExitAllActiveBlocks(endContext);

                // 在最后一帧立即触发完成通知（控制器同步派发 OnMontageEnded）
                OnFinished?.Invoke(this);

                // 若在完成回调中被外部接力或打断退出，直接返回
                if (_state != MontagePlayerState.Playing)
                {
                    return;
                }
            }

            // 阶段 B：后置淡出结束点（NaturalEndTime，彻底静默释放）
            if (nextTime >= naturalEnd)
            {
                _elapsedTime = naturalEnd;
                _lastElapsedTime = _elapsedTime;
                _state = MontagePlayerState.Finished;
                return;
            }
            else
            {
                _elapsedTime = nextTime;
                float sweepTo = Mathf.Min(_elapsedTime, totalDuration);
                float sweepFrom = Mathf.Min(_lastElapsedTime, totalDuration);
                if (sweepTo > sweepFrom || _activeBlockIndices.Count > 0)
                {
                    SweepInterval(sweepFrom, sweepTo, effectiveDelta);
                }
                _lastElapsedTime = _elapsedTime;
            }

            // 3. 更新分段索引并触发跨段事件
            int newSection = _sourceSequence.GetSectionIndexAtTime(_elapsedTime);
            if (newSection != prevSection)
            {
                HandleSectionTransitions(prevSection, newSection);
            }
        }

        /// <summary>
        /// Atomic interval sweep that processes the lifecycle of all action blocks within the time increment range (fromTime, toTime].
        /// Strictly follows the half-open [StartTime, EndTime) invariant so enter/exit are paired and no frame drops are missed.
        /// </summary>
        /// <param name="fromTime">Interval start timestamp (seconds, exclusive)</param>
        /// <param name="toTime">Interval end timestamp (seconds, inclusive)</param>
        /// <param name="effectiveDelta">Effective time step of the current frame</param>
        protected internal virtual void SweepInterval(float fromTime, float toTime, float effectiveDelta)
        {
            if (_sourceSequence == null) return;

            var blocks = _sourceSequence.BakedRuntimeActionBlocks;
            if (blocks == null) return;

            var context = CreateCurrentContext();

            for (int i = 0; i < _configuredBlockCount; i++)
            {
                var blockData = blocks[i];
                if (blockData == null || !blockData.IsEnabled || blockData.Action == null) continue;

                var action = blockData.Action;
                var state = _states[i];
                float start = blockData.StartTime;
                float end = blockData.EndTime;

                bool isInsideNow = toTime >= start && toTime < end;

                // 1. Currently inside [start, end)
                if (isInsideNow)
                {
                    if (!_activeBlockIndices.Contains(i))
                    {
                        action.BlockDuration = blockData.Duration;
                        if (action.CanEnter(context))
                        {
                            action.OnEnter(context, state);
                            _activeBlockIndices.Add(i);
                        }
                    }
                    else
                    {
                        action.BlockDuration = blockData.Duration;
                        action.OnUpdate(context, state, effectiveDelta);
                    }
                }
                // 2. Currently outside the interval but was active -> trigger OnExit and reset the state data
                else if (_activeBlockIndices.Contains(i))
                {
                    action.OnExit(context, state);
                    state?.Reset();
                    _activeBlockIndices.Remove(i);
                }
                // 3. 穿透式扫掠（单帧跨越了整个块区间，或零时长瞬发块恰好落在帧边界）：严格成对触发 OnEnter -> OnExit
                else if ((fromTime <= start && toTime >= end && fromTime < end) || (Mathf.Approximately(start, end) && fromTime <= start && toTime >= start))
                {
                    action.BlockDuration = blockData.Duration;
                    if (action.CanEnter(context))
                    {
                        action.OnEnter(context, state);
                        action.OnExit(context, state);
                        state?.Reset();
                    }
                }
            }
        }

        #endregion

        #region Playback Control & Clock Alignment API

        /// <summary>
        /// Computes the raw target weight of this player's slot in isolation [0.0, 1.0].
        /// Strictly follows the continuous dynamics weight model; on interruption it decays smoothly from the instantaneous weight at that moment.
        /// </summary>
        /// <returns>Continuous target weight of the slot</returns>
        protected internal virtual float CalculateTargetWeight()
        {
            if (_sourceSequence == null || _state == MontagePlayerState.Finished)
            {
                return 0.0f;
            }

            // 1. 外部主动打断退出阶段（Stopping）：从打断时刻的权重线性衰减到 0，保障 C0 连续性
            if (_state == MontagePlayerState.Stopping)
            {
                float blendOutDuration = Mathf.Max(0.0001f, _interruptBlendOutTime);
                float t = Mathf.Clamp01(_currentInterruptTime / blendOutDuration);
                return Mathf.Clamp01(_weightAtInterrupt * (1.0f - t) * _customWeightMultiplier);
            }

            // 2. 正常播放阶段（Playing）：蒙太奇槽位提供 1.0 满额基准，片段内的淡入与外置尾帧衰减完全由切片混合模型（EvaluateAnimationSegments）驱动
            return Mathf.Clamp01(_customWeightMultiplier);
        }

        /// <summary>
        /// Sets the external custom playback weight multiplier [0.0, 1.0].
        /// </summary>
        public virtual void SetCustomWeight(float weight)
        {
            _customWeightMultiplier = Mathf.Clamp01(weight);
        }

        /// <summary>
        /// Synchronizes/sets the target physical duration of the specified physical section and derives the playback rate (configuration only; does not jump the timeline).
        /// If the requested section index is out of range, the request is safely ignored to support single-clip or fallback presentations.
        /// </summary>
        /// <param name="sectionIndex">Physical section index (zero-based)</param>
        /// <param name="targetDuration">Desired real-world duration of this section in seconds</param>
        public virtual void SyncSectionDuration(int sectionIndex, float targetDuration)
        {
            if (_state != MontagePlayerState.Playing || _sourceSequence == null)
            {
                return;
            }

            if (sectionIndex < 0 || sectionIndex >= _sourceSequence.SectionCount)
            {
                // 分段不对应（如单切片保底动作），无视该操作并平稳放行
                return;
            }

            float rawDuration = _sourceSequence.GetSectionDuration(sectionIndex);
            float rate = (rawDuration > 0.0001f && targetDuration > 0.0001f)
                ? (rawDuration / targetDuration)
                : 1.0f;

            _sectionRates[sectionIndex] = Mathf.Max(0.0001f, rate);

            if (_currentSectionIndex == sectionIndex)
            {
                _playbackRate = rate;
            }
        }

        /// <summary>
        /// Adaptive section clock alignment.
        /// Sets the target physical duration of the specified physical section, optionally jumping to its start immediately.
        /// If the requested section does not exist, the operation is safely ignored without interrupting ongoing playback.
        /// </summary>
        public virtual void SyncSection(int sectionIndex, float targetDuration, bool jumpImmediately = false)
        {
            if (_state != MontagePlayerState.Playing || _sourceSequence == null)
            {
                return;
            }

            if (sectionIndex < 0 || sectionIndex >= _sourceSequence.SectionCount)
            {
                // 分段不对应时安全忽略，不中断动画，不抛警告
                return;
            }

            SyncSectionDuration(sectionIndex, targetDuration);

            if (jumpImmediately && _currentSectionIndex != sectionIndex)
            {
                JumpToSection(sectionIndex);
            }
        }

        /// <summary>
        /// Clears the custom rate configuration of the specified section so it falls back to the asset base rate.
        /// </summary>
        /// <param name="sectionIndex">Section index</param>
        public virtual void ClearSectionSync(int sectionIndex)
        {
            if (_state != MontagePlayerState.Playing || _sourceSequence == null)
            {
                return;
            }

            if (sectionIndex < 0 || sectionIndex >= _sourceSequence.SectionCount)
            {
                return;
            }

            _sectionRates.Remove(sectionIndex);
            if (_currentSectionIndex == sectionIndex)
            {
                _playbackRate = 1.0f;
            }
        }

        /// <summary>
        /// Jumps instantly to the start time of the specified section.
        /// If the section does not exist, the jump is safely ignored.
        /// </summary>
        /// <param name="sectionIndex">Physical section index (zero-based)</param>
        public virtual void JumpToSection(int sectionIndex)
        {
            if (_state != MontagePlayerState.Playing || _sourceSequence == null)
            {
                return;
            }

            if (sectionIndex < 0 || sectionIndex >= _sourceSequence.SectionCount)
            {
                // 若请求跳转到 0 但资产未配置显式分段，无条件回退跳转到时间轴起始点 0 秒保底
                if (sectionIndex == 0)
                {
                    JumpToTime(0f);
                }
                return;
            }

            var range = _sourceSequence.GetSectionRange(sectionIndex);
            JumpToTime(range.start);
        }

        /// <summary>
        /// Jumps instantly to the specified absolute timestamp (seconds) and atomically aligns, exits, and activates action block states.
        /// </summary>
        /// <param name="targetTime">Target timestamp (seconds)</param>
        public virtual void JumpToTime(float targetTime)
        {
            if (_state != MontagePlayerState.Playing)
            {
                return;
            }

            float total = TotalDuration;
            float clamped = Mathf.Clamp(targetTime, 0f, total);

            _elapsedTime = clamped;
            _lastElapsedTime = clamped;

            int targetSection = _sourceSequence.GetSectionIndexAtTime(_elapsedTime);
            if (targetSection != _currentSectionIndex)
            {
                _currentSectionIndex = targetSection;
                UpdateSectionPlaybackRate(_currentSectionIndex);
                OnSectionEntered?.Invoke(this, _currentSectionIndex);
            }

            var context = CreateCurrentContext();

            // Re-evaluate the state of all action block collections
            if (_sourceSequence != null)
            {
                var blocks = _sourceSequence.BakedRuntimeActionBlocks;
                for (int i = 0; i < _configuredBlockCount; i++)
                {
                    var blockData = blocks[i];
                    if (blockData == null || !blockData.IsEnabled || blockData.Action == null) continue;

                    var action = blockData.Action;
                    var state = _states[i];
                    bool isInside = _elapsedTime >= blockData.StartTime && _elapsedTime < blockData.EndTime;

                    if (isInside && !_activeBlockIndices.Contains(i))
                    {
                        action.BlockDuration = blockData.Duration;
                        if (action.CanEnter(context))
                        {
                            action.OnEnter(context, state);
                            _activeBlockIndices.Add(i);
                        }
                    }
                    else if (!isInside && _activeBlockIndices.Contains(i))
                    {
                        action.OnExit(context, state);
                        state?.Reset();
                        _activeBlockIndices.Remove(i);
                    }
                }
            }
        }

        /// <summary>
        /// Absolute sampling driven by normalized logical progress.
        /// Maps the normalized progress [0.0, 1.0] of the specified section to physical time and jumps instantly.
        /// If the section does not exist, the operation is safely ignored.
        /// </summary>
        public virtual void EvaluateSectionProgress(int sectionIndex, float progress)
        {
            if (_state != MontagePlayerState.Playing || _sourceSequence == null)
            {
                return;
            }

            if (sectionIndex < 0 || sectionIndex >= _sourceSequence.SectionCount)
            {
                // 分段不存在，安全忽略
                return;
            }

            var range = _sourceSequence.GetSectionRange(sectionIndex);
            float duration = Mathf.Max(0.0001f, range.end - range.start);
            float t = range.start + duration * Mathf.Clamp01(progress);
            JumpToTime(t);
        }

        /// <summary>
        /// Absolute sampling driven by normalized logical progress (compatibility alias).
        /// </summary>
        public virtual void EvaluateSectionAtProgress(int sectionIndex, float progress) => EvaluateSectionProgress(sectionIndex, progress);

        /// <summary>
        /// Evaluates the sample time and normalized blend weight of each animation segment in the mixer at the current authoritative timestamp (zero GC allocation).
        /// Fully supports crossfade transitions between adjacent segments on the timeline.
        /// </summary>
        /// <param name="outIndices">Output active segment indices</param>
        /// <param name="outSampleTimes">Output in-segment sample times (seconds)</param>
        /// <param name="outWeights">Output normalized weights [0.0, 1.0]</param>
        public virtual void EvaluateAnimationSegments(
            List<int> outIndices,
            List<float> outSampleTimes,
            List<float> outWeights)
        {
            if (_sourceSequence == null) return;
            _sourceSequence.EvaluateAnimationSegments(_elapsedTime, outIndices, outSampleTimes, outWeights);
        }

        /// <summary>
        /// Evaluates the animation segments of the specified channel (supports UpperBody, FullBody, Additive).
        /// Zero GC memory allocation.
        /// </summary>
        public virtual void EvaluateChannelSegments(
            MontageLayerChannel channel,
            List<int> outIndices,
            List<float> outSampleTimes,
            List<float> outWeights)
        {
            if (_sourceSequence == null) return;
            _sourceSequence.EvaluateChannelSegments(channel, _elapsedTime, outIndices, outSampleTimes, outWeights);
        }

        /// <summary>
        /// Sets the global playback rate multiplier.
        /// </summary>
        public virtual void SetPlaybackRate(float rate)
        {
            if (_state != MontagePlayerState.Playing)
            {
                return;
            }

            _playbackRate = Mathf.Max(0.0001f, rate);
        }

        /// <summary>
        /// Pauses or resumes playback.
        /// </summary>
        public virtual void SetPaused(bool isPaused)
        {
            if (_state != MontagePlayerState.Playing)
            {
                return;
            }

            _isPaused = isPaused;
        }

        /// <summary>
        /// Sets the current computed weight in the mixer (set by MontageController according to the Dominant mixing topology).
        /// </summary>
        protected internal virtual void SetCurrentWeight(float weight)
        {
            _currentWeight = Mathf.Clamp01(weight);
        }

        /// <summary>
        /// Interrupts and stops this montage from outside, entering the fade-out phase and safely exiting all active action blocks.
        /// </summary>
        /// <param name="customBlendOutTime">Custom blend-out duration (uses the asset default when null)</param>
        public virtual void Stop(float? customBlendOutTime = null)
        {
            if (_state != MontagePlayerState.Playing) return;

            bool shouldNotifyInterrupt = !_isLogicalFinished;

            // 锁存打断瞬间的平滑权重，消除外部图层覆盖影响
            _weightAtInterrupt = Mathf.Clamp01(CalculateTargetWeight());
            _state = MontagePlayerState.Stopping;
            _interruptBlendOutTime = Mathf.Max(0.0001f, customBlendOutTime ?? 0.1f);
            _currentInterruptTime = 0f;

            var context = CreateCurrentContext();
            ExitAllActiveBlocks(context);

            if (shouldNotifyInterrupt)
            {
                OnInterrupted?.Invoke(this);
            }
        }

        /// <summary>
        /// Terminates playback completely and marks it finished immediately, ensuring all active action blocks exit safely.
        /// </summary>
        protected internal virtual void Terminate()
        {
            if (_state == MontagePlayerState.Finished) return;

            var context = CreateCurrentContext();
            ExitAllActiveBlocks(context);

            bool shouldNotifyInterrupt = (_state == MontagePlayerState.Playing && !_isLogicalFinished);
            _state = MontagePlayerState.Finished;

            if (shouldNotifyInterrupt)
            {
                OnInterrupted?.Invoke(this);
            }
        }

        #endregion

        #region Internal Helper Methods

        protected virtual void HandleSectionTransitions(int fromSection, int toSection)
        {
            if (fromSection < toSection)
            {
                for (int s = fromSection + 1; s <= toSection; s++)
                {
                    _currentSectionIndex = s;
                    UpdateSectionPlaybackRate(s);
                    OnSectionEntered?.Invoke(this, s);
                }
            }
            else if (fromSection > toSection)
            {
                for (int s = fromSection - 1; s >= toSection; s--)
                {
                    _currentSectionIndex = s;
                    UpdateSectionPlaybackRate(s);
                    OnSectionEntered?.Invoke(this, s);
                }
            }
        }

        protected virtual void UpdateSectionPlaybackRate(int sectionIndex)
        {
            if (_sectionRates.TryGetValue(sectionIndex, out float customRate))
            {
                _playbackRate = customRate;
            }
            else
            {
                _playbackRate = 1.0f;
            }
        }

        protected virtual void CompleteNaturalFinish(in MontageActionContext context)
        {
            if (_state != MontagePlayerState.Playing) return;

            _state = MontagePlayerState.Finished;
            ExitAllActiveBlocks(context);

            OnFinished?.Invoke(this);
        }

        /// <summary>
        /// Terminates playback completely, clears all state and bound delegates, and returns state instances to the pool (for micro-pool reuse).
        /// </summary>
        public virtual void Reset()
        {
            ResetStates();
            _sourceSequence = null;
            _state = MontagePlayerState.Finished;
            _isPaused = false;
            _isLogicalFinished = false;
            _elapsedTime = 0f;
            _lastElapsedTime = -0.0001f;
            _currentWeight = 0f;
            _weightAtInterrupt = 1.0f;
            _interruptBlendOutTime = 0.1f;
            _currentInterruptTime = 0f;
            _customWeightMultiplier = 1.0f;
            _sectionRates.Clear();

            OnFinished = null;
            OnInterrupted = null;
            OnSectionEntered = null;
        }

        protected virtual void ExitAllActiveBlocks(in MontageActionContext context)
        {
            if (_activeBlockIndices.Count == 0 || _sourceSequence == null) return;

            var blocks = _sourceSequence.BakedRuntimeActionBlocks;
            if (blocks == null) return;

            foreach (int index in _activeBlockIndices)
            {
                if (index >= 0 && index < _configuredBlockCount && index < blocks.Count)
                {
                    var action = blocks[index].Action;
                    var state = _states[index];
                    action?.OnExit(context, state);
                    state?.Reset();
                }
            }

            _activeBlockIndices.Clear();
        }

        protected virtual MontageActionContext CreateCurrentContext()
        {
            float total = TotalDuration;
            float normProgress = total > 0.0001f ? Mathf.Clamp01(_elapsedTime / total) : 0f;

            var range = _sourceSequence.GetSectionRange(_currentSectionIndex);
            float sectionLen = Mathf.Max(0.0001f, range.end - range.start);
            float sectionProgress = Mathf.Clamp01((_elapsedTime - range.start) / sectionLen);

            return new MontageActionContext(
                _targetObject,
                _targetAnimator,
                _elapsedTime,
                total,
                _currentSectionIndex,
                sectionProgress,
                normProgress,
                _playbackRate,
                _isPreview,
                _controller);
        }

        #endregion
    }
}
