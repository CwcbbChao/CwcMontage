using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 蒙太奇播放器运行生命周期状态。
    /// </summary>
    public enum MontagePlayerState
    {
        /// <summary>
        /// 正在正常播放（包含淡入、稳定播放、分段循环与尾部自然淡出阶段）。
        /// </summary>
        Playing,

        /// <summary>
        /// 接收到外部主动打断指令，正在执行平滑淡出。
        /// </summary>
        Stopping,

        /// <summary>
        /// 播放已彻底完成（已自然播完或淡出归零）。
        /// </summary>
        Finished
    }

    /// <summary>
    /// 纯 C# 运行时蒙太奇播放器实例。
    /// 作为单一权威时钟源（Master Clock），维护动画时间流逝、动力学权重计算、原子化区间扫掠判定与动作块生命周期。
    /// </summary>
    public class MontagePlayer
    {
        #region 私有字段

        private readonly MontageSequenceSO _sourceAsset;
        private readonly GameObject _targetObject;
        private readonly Animator _targetAnimator;
        private readonly bool _isPreview;

        private readonly List<MontageActionBlockData> _runtimeBlocks = new(16);
        private readonly HashSet<MontageActionBlockData> _activeBlocks = new();
        private readonly List<MontageActionBlockData> _tempSweepList = new(16);
        private readonly Dictionary<int, float> _sectionRates = new();

        private MontagePlayerState _state = MontagePlayerState.Playing;
        private float _elapsedTime;
        private float _lastElapsedTime;
        private float _playbackRate = 1.0f;
        private float _currentWeight;
        private float _weightAtStop = 1.0f;

        private float _blendInTime;
        private AnimationCurve _blendInCurve;
        private float _blendOutTime;
        private AnimationCurve _blendOutCurve;
        private float _currentBlendInTime;
        private float _currentBlendOutTime;

        private int _currentSectionIndex;

        private bool _isPaused;

        #endregion

        #region 公共属性

        /// <summary>
        /// 关联的源蒙太奇配置资产。
        /// </summary>
        public MontageSequenceSO SourceAsset => _sourceAsset;

        /// <summary>
        /// 目标宿主 GameObject。
        /// </summary>
        public GameObject TargetObject => _targetObject;

        /// <summary>
        /// 目标 Animator。
        /// </summary>
        public Animator TargetAnimator => _targetAnimator;

        /// <summary>
        /// 当前播放器所处的生命周期状态。
        /// </summary>
        public MontagePlayerState State => _state;

        /// <summary>
        /// 当前播放进行到的绝对时间戳（秒，单一权威时钟源）。
        /// </summary>
        public float ElapsedTime => _elapsedTime;

        /// <summary>
        /// 上一次扫掠的绝对时间戳（秒）。
        /// </summary>
        public float LastElapsedTime => _lastElapsedTime;

        /// <summary>
        /// 供底层 Playable 进行骨骼采样的绝对时间戳（秒，自动 Clamp/Wrap 动作时长）。
        /// </summary>
        public float ClipSampleTime
        {
            get
            {
                if (_sourceAsset == null || TotalDuration <= 0.0001f) return 0f;
                if (_sourceAsset.IsLooping)
                {
                    return _elapsedTime % TotalDuration;
                }
                return Mathf.Min(_elapsedTime, TotalDuration);
            }
        }

        /// <summary>
        /// 当前播放速率倍率。
        /// </summary>
        public float PlaybackRate => _playbackRate;

        /// <summary>
        /// 当前该动画在混音器中的计算权重 [0.0, 1.0]。
        /// </summary>
        public float CurrentWeight => _currentWeight;

        /// <summary>
        /// 当前所处的物理分段索引。
        /// </summary>
        public int CurrentSectionIndex => _currentSectionIndex;

        /// <summary>
        /// 是否处于正常播放状态。
        /// </summary>
        public bool IsPlaying => _state == MontagePlayerState.Playing;

        /// <summary>
        /// 是否处于主动打断淡出停止中。
        /// </summary>
        public bool IsStopping => _state == MontagePlayerState.Stopping;

        /// <summary>
        /// 是否已彻底播放完成。
        /// </summary>
        public bool IsFinished => _state == MontagePlayerState.Finished;

        /// <summary>
        /// 是否已停止（处于主动淡出中或已彻底结束）。
        /// </summary>
        public bool IsStopped => _state == MontagePlayerState.Stopping || _state == MontagePlayerState.Finished;

        /// <summary>
        /// 是否处于暂停状态。
        /// </summary>
        public bool IsPaused => _isPaused;

        /// <summary>
        /// 当前动画核心片段时长（秒）。
        /// </summary>
        public float TotalDuration => _sourceAsset != null ? _sourceAsset.TotalDuration : 0f;

        /// <summary>
        /// 当前动画单次播放的自然生存终点（秒，包含推迟淡出时间）。
        /// </summary>
        public float NaturalEndTime => _sourceAsset != null ? _sourceAsset.NaturalEndTime : 0f;

        /// <summary>
        /// 淡入总时长（秒）。
        /// </summary>
        public float BlendInTime => _blendInTime;

        /// <summary>
        /// 淡出总时长（秒）。
        /// </summary>
        public float BlendOutTime => _blendOutTime;

        /// <summary>
        /// 当前已流逝的淡入时长（秒）。
        /// </summary>
        public float CurrentBlendInTime => _currentBlendInTime;

        /// <summary>
        /// 当前已流逝的淡出时长（秒）。
        /// </summary>
        public float CurrentBlendOutTime => _currentBlendOutTime;

        #endregion

        #region 公共事件

        /// <summary>
        /// 当动画自然播放完成时触发。
        /// </summary>
        public event Action<MontagePlayer> OnFinished;

        /// <summary>
        /// 当动画被外部主动打断停止时触发。
        /// </summary>
        public event Action<MontagePlayer> OnInterrupted;

        /// <summary>
        /// 当时间轴跨越物理分段切分点时触发（参数为新分段索引）。
        /// </summary>
        public event Action<MontagePlayer, int> OnSectionEntered;

        #endregion

        #region 构造方法

        public MontagePlayer(
            MontageSequenceSO sourceAsset,
            GameObject targetObject,
            Animator targetAnimator,
            float? customBlendInTime = null,
            AnimationCurve customBlendInCurve = null,
            bool isPreview = false)
        {
            if (sourceAsset == null)
            {
                Debug.LogError("[MontagePlayer] 无法创建播放器实例：sourceAsset 为空。");
                return;
            }

            _sourceAsset = sourceAsset;
            _targetObject = targetObject;
            _targetAnimator = targetAnimator;
            _isPreview = isPreview;

            _state = MontagePlayerState.Playing;
            _isPaused = false;

            _elapsedTime = 0f;
            _lastElapsedTime = -0.0001f;
            _playbackRate = _sourceAsset.BasePlayRate;

            _blendInTime = customBlendInTime ?? _sourceAsset.DefaultBlendInTime;
            _blendInCurve = customBlendInCurve ?? _sourceAsset.BlendInCurve;
            _blendOutTime = _sourceAsset.DefaultBlendOutTime;
            _blendOutCurve = _sourceAsset.BlendOutCurve;

            _currentBlendInTime = 0f;
            _currentBlendOutTime = 0f;
            _currentWeight = 0f;
            _weightAtStop = 1.0f;
            _currentSectionIndex = 0;

            // 克隆所有非静音轨道上的动作块并进行合法性校验
            _runtimeBlocks.Clear();
            if (_sourceAsset.Tracks != null)
            {
                float fps = _sourceAsset.FrameRate;
                for (int i = 0; i < _sourceAsset.Tracks.Count; i++)
                {
                    var track = _sourceAsset.Tracks[i];
                    if (track == null || track.IsMuted || track.ActionBlocks == null) continue;

                    for (int j = 0; j < track.ActionBlocks.Count; j++)
                    {
                        var blockData = track.ActionBlocks[j];
                        if (blockData != null && blockData.IsEnabled && blockData.Action != null)
                        {
                            var cloned = blockData.Clone();
                            cloned.EnsureValid(fps);
                            _runtimeBlocks.Add(cloned);
                        }
                    }
                }
            }
        }

        #endregion

        #region 核心更新逻辑 (Tick & Sweep)

        /// <summary>
        /// 帧更新方法，由 MontageCoordinator 或编辑器 UI 在 Update 中调用。
        /// 驱动权威时间轴步进、淡入淡出计算、分段切换与原子化区间扫掠生命周期分发。
        /// </summary>
        /// <param name="deltaTime">时间步长（秒）</param>
        internal void Tick(float deltaTime)
        {
            if (_state == MontagePlayerState.Finished || _isPaused)
            {
                return;
            }

            // 1. 推进淡入计时
            if (_currentBlendInTime < _blendInTime && _blendInTime > 0.0001f)
            {
                _currentBlendInTime = Mathf.Min(_currentBlendInTime + deltaTime, _blendInTime);
            }

            // 2. 若处于主动打断淡出阶段，推进淡出计时并检查彻底终结
            if (_state == MontagePlayerState.Stopping)
            {
                _currentBlendOutTime = Mathf.Min(_currentBlendOutTime + deltaTime, _blendOutTime);
                if (_currentBlendOutTime >= _blendOutTime || CalculateTargetWeight() <= 0.0001f)
                {
                    _state = MontagePlayerState.Finished;
                }
                return;
            }

            // 3. 正常播放期：推进时间轴时间
            int prevSection = _currentSectionIndex;
            float effectiveDelta = deltaTime * _playbackRate;

            // 全局循环模式
            if (_sourceAsset.IsLooping && TotalDuration > 0.0001f)
            {
                float totalDuration = TotalDuration;
                float nextTime = _elapsedTime + effectiveDelta;

                if (nextTime >= totalDuration)
                {
                    // 原子阶段 1：尾部扫掠
                    SweepInterval(_lastElapsedTime, totalDuration, effectiveDelta);

                    // 时间回绕
                    float wrappedTime = nextTime % totalDuration;
                    _elapsedTime = wrappedTime;
                    _lastElapsedTime = 0f;

                    // 原子阶段 2：头部扫掠
                    SweepInterval(-0.0001f, wrappedTime, effectiveDelta);
                    _lastElapsedTime = _elapsedTime;
                }
                else
                {
                    _elapsedTime = nextTime;
                    SweepInterval(_lastElapsedTime, _elapsedTime, effectiveDelta);
                    _lastElapsedTime = _elapsedTime;
                }
            }
            // 单次播放模式（以 NaturalEndTime 为权威自然终点）
            else
            {
                float naturalEnd = _sourceAsset.NaturalEndTime;
                float nextTime = _elapsedTime + effectiveDelta;

                if (nextTime >= naturalEnd)
                {
                    _elapsedTime = naturalEnd;
                    SweepInterval(_lastElapsedTime, _elapsedTime, effectiveDelta);
                    _lastElapsedTime = _elapsedTime;

                    int finalSection = _sourceAsset.GetSectionIndexAtTime(_elapsedTime);
                    if (finalSection != prevSection)
                    {
                        HandleSectionTransitions(prevSection, finalSection);
                    }

                    var endContext = CreateCurrentContext();
                    CompleteNaturalFinish(endContext);
                    return;
                }
                else
                {
                    _elapsedTime = nextTime;
                    SweepInterval(_lastElapsedTime, _elapsedTime, effectiveDelta);
                    _lastElapsedTime = _elapsedTime;

                    // 边界保护：若到达 clip 时长且计算权重已归零（提前淡出完毕）
                    if (_elapsedTime >= _sourceAsset.TotalDuration && CalculateTargetWeight() <= 0.0001f)
                    {
                        int finalSection = _sourceAsset.GetSectionIndexAtTime(_elapsedTime);
                        if (finalSection != prevSection)
                        {
                            HandleSectionTransitions(prevSection, finalSection);
                        }

                        var endContext = CreateCurrentContext();
                        CompleteNaturalFinish(endContext);
                        return;
                    }
                }
            }

            // 4. 更新分段索引并连续迭代触发分段跨越事件
            int newSection = _sourceAsset.GetSectionIndexAtTime(_elapsedTime);
            if (newSection != prevSection)
            {
                HandleSectionTransitions(prevSection, newSection);
            }
        }

        /// <summary>
        /// 原子区间扫掠算法，处理时间增量范围 (fromTime, toTime] 内的所有动作块生命周期。
        /// 严格遵循 [StartTime, EndTime) 半开半闭区间不变式，确保成对进入/退出，杜绝掉帧漏检。
        /// </summary>
        /// <param name="fromTime">区间起点时间戳（秒，开区间）</param>
        /// <param name="toTime">区间终点时间戳（秒，闭区间）</param>
        /// <param name="effectiveDelta">当前帧有效时间步长</param>
        internal void SweepInterval(float fromTime, float toTime, float effectiveDelta)
        {
            var context = CreateCurrentContext();

            for (int i = 0; i < _runtimeBlocks.Count; i++)
            {
                var blockData = _runtimeBlocks[i];
                if (blockData == null || !blockData.IsEnabled || blockData.Action == null) continue;

                var action = blockData.Action;
                float start = blockData.StartTime;
                float end = blockData.EndTime;

                bool isInsideNow = toTime >= start && toTime < end;

                // 1. 当前处于 [start, end) 区间内
                if (isInsideNow)
                {
                    if (!_activeBlocks.Contains(blockData))
                    {
                        if (action.CanEnter(context))
                        {
                            action.OnEnter(context);
                            _activeBlocks.Add(blockData);
                        }
                    }
                    else
                    {
                        action.OnUpdate(context, effectiveDelta);
                    }
                }
                // 2. 当前已离开区间，但之前已激活 -> 触发 OnExit
                else if (_activeBlocks.Contains(blockData))
                {
                    action.OnExit(context);
                    _activeBlocks.Remove(blockData);
                }
                // 3. 穿透扫掠（单帧跨越整个块区间）：严格成对触发 OnEnter -> OnExit
                else if (fromTime <= start && toTime >= end && fromTime < end)
                {
                    if (action.CanEnter(context))
                    {
                        action.OnEnter(context);
                        action.OnExit(context);
                    }
                }
            }
        }

        #endregion

        #region 控制与时钟对齐 API

        /// <summary>
        /// 计算当前播放器单槽内聚的原始目标权重 [0.0, 1.0]。
        /// 严格遵循连续动力学权重模型，打断时以打断瞬间实际瞬时权重为起点平滑衰减。
        /// </summary>
        /// <returns>单槽连续目标权重</returns>
        internal float CalculateTargetWeight()
        {
            if (_sourceAsset == null || _state == MontagePlayerState.Finished)
            {
                return 0.0f;
            }

            // 1. 主动打断停止阶段（Stopping）：从打断瞬间权重 _weightAtStop 衰减至 0，保证 C0 连续性
            if (_state == MontagePlayerState.Stopping)
            {
                float blendOutDuration = Mathf.Max(0.0001f, _blendOutTime);
                float t = Mathf.Clamp01(_currentBlendOutTime / blendOutDuration);
                float decay = _blendOutCurve != null ? Mathf.Clamp01(1f - _blendOutCurve.Evaluate(t)) : (1f - t);
                return Mathf.Clamp01(_weightAtStop * decay);
            }

            // 2. 正常播放期（Playing）
            float inWeight = 1.0f;
            float blendInDuration = Mathf.Max(0.0001f, _blendInTime);
            if (_currentBlendInTime < blendInDuration && blendInDuration > 0.0001f)
            {
                float t = Mathf.Clamp01(_currentBlendInTime / blendInDuration);
                inWeight = _blendInCurve != null ? Mathf.Clamp01(_blendInCurve.Evaluate(t)) : t;
            }

            // 2.1 自然尾部淡出阶段（仅在单次非循环模式下触发）
            float outWeight = 1.0f;
            float totalDuration = TotalDuration;
            if (!_sourceAsset.IsLooping && totalDuration > 0.0001f)
            {
                float naturalBlendOutDuration = Mathf.Max(0.0001f, _sourceAsset.DefaultBlendOutTime);
                float blendOutStart = totalDuration - naturalBlendOutDuration + _sourceAsset.BlendOutOffset;
                if (_elapsedTime >= blendOutStart && naturalBlendOutDuration > 0.0001f)
                {
                    float t = Mathf.Clamp01((_elapsedTime - blendOutStart) / naturalBlendOutDuration);
                    outWeight = _sourceAsset.BlendOutCurve != null ? Mathf.Clamp01(1f - _sourceAsset.BlendOutCurve.Evaluate(t)) : (1f - t);
                }
            }

            return Mathf.Min(inWeight, outWeight);
        }

        /// <summary>
        /// 同步/设置指定物理分段的目标物理时长并换算速率倍率（纯配置，不产生时间轴跳转）。
        /// </summary>
        /// <param name="sectionIndex">物理分段索引（从 0 开始）</param>
        /// <param name="targetDuration">期望该分段在游戏中持续的真实秒数</param>
        public void SyncSectionDuration(int sectionIndex, float targetDuration)
        {
            if (_state != MontagePlayerState.Playing)
            {
                return;
            }

            if (sectionIndex < 0 || sectionIndex >= _sourceAsset.SectionCount)
            {
                Debug.LogWarning($"[MontagePlayer] 目标分段索引 {sectionIndex} 越界 (有效范围: 0 ~ {_sourceAsset.SectionCount - 1})。");
                return;
            }

            float rawDuration = _sourceAsset.GetSectionDuration(sectionIndex);
            float rate = (rawDuration > 0.0001f && targetDuration > 0.0001f)
                ? (rawDuration / targetDuration)
                : _sourceAsset.BasePlayRate;

            _sectionRates[sectionIndex] = Mathf.Max(0.0001f, rate);

            if (_currentSectionIndex == sectionIndex)
            {
                _playbackRate = rate;
            }
        }

        /// <summary>
        /// 自适应分段时钟对齐。
        /// 设置指定物理分段的目标物理时长，可选择是否立即跳转至该分段起点。
        /// </summary>
        public void SyncSection(int sectionIndex, float targetDuration, bool jumpImmediately = false)
        {
            if (_state != MontagePlayerState.Playing)
            {
                return;
            }

            SyncSectionDuration(sectionIndex, targetDuration);

            if (jumpImmediately && _currentSectionIndex != sectionIndex)
            {
                JumpToSection(sectionIndex);
            }
        }

        /// <summary>
        /// 清除指定分段的自定义速率配置，使其恢复为资产基础速率。
        /// </summary>
        /// <param name="sectionIndex">分段索引</param>
        public void ClearSectionSync(int sectionIndex)
        {
            if (_state != MontagePlayerState.Playing)
            {
                return;
            }

            _sectionRates.Remove(sectionIndex);
            if (_currentSectionIndex == sectionIndex)
            {
                _playbackRate = _sourceAsset.BasePlayRate;
            }
        }

        /// <summary>
        /// 瞬间跳转到指定分段的起始时间点。
        /// </summary>
        /// <param name="sectionIndex">物理分段索引（从 0 开始）</param>
        public void JumpToSection(int sectionIndex)
        {
            if (_state != MontagePlayerState.Playing)
            {
                return;
            }

            var range = _sourceAsset.GetSectionRange(sectionIndex);
            JumpToTime(range.start);
        }

        /// <summary>
        /// 瞬间跳转到指定的绝对时间戳（秒），并自动完成动作块状态的原子对齐与退出/激活。
        /// </summary>
        /// <param name="targetTime">目标时间戳（秒）</param>
        public void JumpToTime(float targetTime)
        {
            if (_state != MontagePlayerState.Playing)
            {
                return;
            }

            float total = TotalDuration;
            float clamped = Mathf.Clamp(targetTime, 0f, total);

            _elapsedTime = clamped;
            _lastElapsedTime = clamped;

            int targetSection = _sourceAsset.GetSectionIndexAtTime(_elapsedTime);
            if (targetSection != _currentSectionIndex)
            {
                _currentSectionIndex = targetSection;
                UpdateSectionPlaybackRate(_currentSectionIndex);
                OnSectionEntered?.Invoke(this, _currentSectionIndex);
            }

            var context = CreateCurrentContext();

            // 重新评估所有动作块集合状态
            for (int i = 0; i < _runtimeBlocks.Count; i++)
            {
                var blockData = _runtimeBlocks[i];
                if (blockData == null || !blockData.IsEnabled || blockData.Action == null) continue;

                var action = blockData.Action;
                bool isInside = _elapsedTime >= blockData.StartTime && _elapsedTime < blockData.EndTime;

                if (isInside && !_activeBlocks.Contains(blockData))
                {
                    if (action.CanEnter(context))
                    {
                        action.OnEnter(context);
                        _activeBlocks.Add(blockData);
                    }
                }
                else if (!isInside && _activeBlocks.Contains(blockData))
                {
                    action.OnExit(context);
                    _activeBlocks.Remove(blockData);
                }
            }
        }

        /// <summary>
        /// 逻辑归一化进度绝对驱动采样。
        /// 将指定分段的归一化进度 [0.0, 1.0] 映射为物理时间并执行瞬间跳转。
        /// </summary>
        public void EvaluateSectionProgress(int sectionIndex, float progress)
        {
            if (_state != MontagePlayerState.Playing)
            {
                return;
            }

            var range = _sourceAsset.GetSectionRange(sectionIndex);
            float duration = Mathf.Max(0.0001f, range.end - range.start);
            float t = range.start + duration * Mathf.Clamp01(progress);
            JumpToTime(t);
        }

        /// <summary>
        /// 逻辑归一化进度绝对驱动采样（兼容别名）。
        /// </summary>
        public void EvaluateSectionAtProgress(int sectionIndex, float progress) => EvaluateSectionProgress(sectionIndex, progress);

        /// <summary>
        /// 设置全局播放速率倍率。
        /// </summary>
        public void SetPlaybackRate(float rate)
        {
            if (_state != MontagePlayerState.Playing)
            {
                return;
            }

            _playbackRate = Mathf.Max(0.0001f, rate);
        }

        /// <summary>
        /// 暂停或恢复播放。
        /// </summary>
        public void SetPaused(bool isPaused)
        {
            if (_state != MontagePlayerState.Playing)
            {
                return;
            }

            _isPaused = isPaused;
        }

        /// <summary>
        /// 设置当前在混音器中的计算权重（由 MontageCoordinator 依据 Dominant 混音拓扑设置）。
        /// </summary>
        internal void SetCurrentWeight(float weight)
        {
            _currentWeight = Mathf.Clamp01(weight);
        }

        /// <summary>
        /// 外部主动打断停止该蒙太奇播放，进入淡出阶段并安全退出所有活跃动作块。
        /// </summary>
        /// <param name="customBlendOutTime">自定义淡出时长（若为空使用资产默认配置）</param>
        public void Stop(float? customBlendOutTime = null)
        {
            if (_state != MontagePlayerState.Playing) return;

            // 锁存打断瞬间的实际权重，保证淡出曲线具备连续性
            _weightAtStop = Mathf.Clamp01(_currentWeight);
            _state = MontagePlayerState.Stopping;
            _blendOutTime = Mathf.Max(0.0001f, customBlendOutTime ?? _sourceAsset.DefaultBlendOutTime);
            _currentBlendOutTime = 0f;

            var context = CreateCurrentContext();
            ExitAllActiveBlocks(context);

            OnInterrupted?.Invoke(this);
        }

        /// <summary>
        /// 彻底终止播放并立即标记为已完成，确保所有活跃动作块百分之百安全退出。
        /// </summary>
        internal void Terminate()
        {
            if (_state == MontagePlayerState.Finished) return;

            var context = CreateCurrentContext();
            ExitAllActiveBlocks(context);

            bool wasPlaying = (_state == MontagePlayerState.Playing);
            _state = MontagePlayerState.Finished;

            if (wasPlaying)
            {
                OnInterrupted?.Invoke(this);
            }
        }

        #endregion

        #region 私有辅助方法

        private void HandleSectionTransitions(int fromSection, int toSection)
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

        private void UpdateSectionPlaybackRate(int sectionIndex)
        {
            if (_sectionRates.TryGetValue(sectionIndex, out float customRate))
            {
                _playbackRate = customRate;
            }
            else
            {
                _playbackRate = _sourceAsset.BasePlayRate;
            }
        }

        private void CompleteNaturalFinish(in MontageActionContext context)
        {
            if (_state != MontagePlayerState.Playing) return;

            _state = MontagePlayerState.Finished;
            ExitAllActiveBlocks(context);

            OnFinished?.Invoke(this);
        }

        private void ExitAllActiveBlocks(in MontageActionContext context)
        {
            if (_activeBlocks.Count == 0) return;

            _tempSweepList.Clear();
            foreach (var block in _activeBlocks)
            {
                _tempSweepList.Add(block);
            }

            for (int i = 0; i < _tempSweepList.Count; i++)
            {
                _tempSweepList[i].Action?.OnExit(context);
            }

            _activeBlocks.Clear();
            _tempSweepList.Clear();
        }

        private MontageActionContext CreateCurrentContext()
        {
            float total = TotalDuration;
            float normProgress = total > 0.0001f ? Mathf.Clamp01(_elapsedTime / total) : 0f;

            var range = _sourceAsset.GetSectionRange(_currentSectionIndex);
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
                _isPreview);
        }

        #endregion
    }
}
