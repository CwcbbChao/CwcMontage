using System;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 蒙太奇播放智能轻量级结构体句柄（值类型，纯栈分配，零 GC）。
    /// 内部封装代际版本号（Generation ID），当动画结束或槽位被复用时自动失效，
    /// 彻底杜绝野指针、悬挂引用与对象池串号误操作。
    /// </summary>
    public readonly struct MontageHandle : IEquatable<MontageHandle>
    {
        #region 私有字段

        private readonly MontageCoordinator _coordinator;
        private readonly int _layerIndex;
        private readonly int _slotIndex;
        private readonly int _generation;

        #endregion

        #region 公共属性

        /// <summary>
        /// 无效默认句柄。
        /// </summary>
        public static MontageHandle Invalid => default;

        /// <summary>
        /// 当前句柄是否处于有效播放状态（动画仍在播放且代际版本完全匹配）。
        /// </summary>
        public bool IsValid => _coordinator != null && _coordinator.IsHandleValid(_layerIndex, _slotIndex, _generation);

        /// <summary>
        /// 当前是否处于正常播放中。
        /// </summary>
        public bool IsPlaying => _coordinator != null && _coordinator.IsHandlePlaying(_layerIndex, _slotIndex, _generation);

        /// <summary>
        /// 当前是否处于主动打断淡出阶段。
        /// </summary>
        public bool IsStopping => _coordinator != null && _coordinator.IsHandleStopping(_layerIndex, _slotIndex, _generation);

        /// <summary>
        /// 当前句柄所指向的动画是否已彻底播放完成或已停止。
        /// </summary>
        public bool IsFinished => _coordinator != null && _coordinator.IsHandleFinished(_layerIndex, _slotIndex, _generation);

        /// <summary>
        /// 当前是否处于暂停状态。若句柄无效则返回 false。
        /// </summary>
        public bool IsPaused => _coordinator != null && _coordinator.IsHandlePaused(_layerIndex, _slotIndex, _generation);

        /// <summary>
        /// 当前动画在混音器中的计算权重 [0.0, 1.0]。若句柄无效则返回 0。
        /// </summary>
        public float CurrentWeight => _coordinator != null ? _coordinator.GetHandleWeight(_layerIndex, _slotIndex, _generation) : 0f;

        /// <summary>
        /// 当前动画总时长（秒）。若句柄无效则返回 0。
        /// </summary>
        public float TotalDuration => _coordinator != null ? _coordinator.GetHandleTotalDuration(_layerIndex, _slotIndex, _generation) : 0f;

        /// <summary>
        /// 当前播放已流逝的绝对时间（秒）。若句柄无效则返回 0。
        /// </summary>
        public float ElapsedTime => _coordinator != null ? _coordinator.GetHandleElapsedTime(_layerIndex, _slotIndex, _generation) : 0f;

        /// <summary>
        /// 当前动画剩余播放时长（秒）。若句柄无效则返回 0。
        /// </summary>
        public float RemainingTime => Mathf.Max(0f, TotalDuration - ElapsedTime);

        /// <summary>
        /// 当前动画归一化播放进度 [0.0, 1.0]。若句柄无效或总时长为 0 则返回 0。
        /// </summary>
        public float NormalizedTime
        {
            get
            {
                float total = TotalDuration;
                return total > 0.0001f ? Mathf.Clamp01(ElapsedTime / total) : 0f;
            }
        }

        /// <summary>
        /// 当前动画归一化播放进度（NormalizedTime 的便捷别名）[0.0, 1.0]。
        /// </summary>
        public float Progress => NormalizedTime;

        /// <summary>
        /// 当前所处的物理分段索引。若句柄无效则返回 -1。
        /// </summary>
        public int CurrentSectionIndex => _coordinator != null ? _coordinator.GetHandleSectionIndex(_layerIndex, _slotIndex, _generation) : -1;

        /// <summary>
        /// 当前蒙太奇包含的物理分段总数量。若句柄无效则返回 0。
        /// </summary>
        public int SectionCount => SourceAsset != null ? SourceAsset.SectionCount : 0;

        /// <summary>
        /// 关联的源蒙太奇配置资产。若句柄无效或已覆写则返回 null。
        /// </summary>
        public MontageSequenceSO SourceAsset => _coordinator != null ? _coordinator.GetHandleSourceAsset(_layerIndex, _slotIndex, _generation) : null;

        /// <summary>
        /// 绑定的协调器组件。
        /// </summary>
        public MontageCoordinator Coordinator => _coordinator;

        /// <summary>
        /// 目标图层索引。
        /// </summary>
        public int LayerIndex => _layerIndex;

        /// <summary>
        /// 目标双缓冲槽索引。
        /// </summary>
        public int SlotIndex => _slotIndex;

        /// <summary>
        /// 代际版本号。
        /// </summary>
        public int Generation => _generation;

        #endregion

        #region 构造方法

        internal MontageHandle(MontageCoordinator coordinator, int layerIndex, int slotIndex, int generation)
        {
            _coordinator = coordinator;
            _layerIndex = layerIndex;
            _slotIndex = slotIndex;
            _generation = generation;
        }

        #endregion

        #region 公共控制与查询方法 (安全转发，无效时自动静默拦截)

        /// <summary>
        /// 瞬间跳转到指定分段的起始时间点。
        /// </summary>
        /// <param name="sectionIndex">物理分段索引</param>
        public void JumpToSection(int sectionIndex)
        {
            if (_coordinator != null)
            {
                _coordinator.JumpToSection(_layerIndex, _slotIndex, _generation, sectionIndex);
            }
        }

        /// <summary>
        /// 瞬间跳转到指定的绝对时间戳（秒），并自动完成动作块状态的原子对齐。
        /// </summary>
        /// <param name="targetTime">目标时间戳（秒）</param>
        public void JumpToTime(float targetTime)
        {
            if (_coordinator != null)
            {
                _coordinator.JumpToTime(_layerIndex, _slotIndex, _generation, targetTime);
            }
        }

        /// <summary>
        /// 逻辑归一化进度绝对驱动采样。
        /// 将指定分段的归一化进度 [0.0, 1.0] 映射为物理时间并执行瞬间跳转。
        /// </summary>
        /// <param name="sectionIndex">物理分段索引</param>
        /// <param name="progress">分段进度 [0.0, 1.0]</param>
        public void EvaluateSectionProgress(int sectionIndex, float progress)
        {
            if (_coordinator != null)
            {
                _coordinator.EvaluateSectionProgress(_layerIndex, _slotIndex, _generation, sectionIndex, progress);
            }
        }

        /// <summary>
        /// 逻辑归一化进度绝对驱动采样（兼容别名）。
        /// </summary>
        public void EvaluateSectionAtProgress(int sectionIndex, float progress) => EvaluateSectionProgress(sectionIndex, progress);

        /// <summary>
        /// 同步/设置指定物理分段的目标物理时长并换算速率倍率（纯配置，不产生时间轴跳转）。
        /// </summary>
        /// <param name="sectionIndex">物理分段索引</param>
        /// <param name="targetDuration">期望该分段在游戏中持续的真实秒数</param>
        public void SyncSectionDuration(int sectionIndex, float targetDuration)
        {
            if (_coordinator != null)
            {
                _coordinator.SyncSectionDuration(_layerIndex, _slotIndex, _generation, sectionIndex, targetDuration);
            }
        }

        /// <summary>
        /// 自适应分段时钟对齐。
        /// 设置指定物理分段的目标物理时长，可选择是否立即跳转至该分段起点。
        /// </summary>
        /// <param name="sectionIndex">物理分段索引</param>
        /// <param name="targetDuration">期望该分段在游戏中持续的真实秒数</param>
        /// <param name="jumpImmediately">是否立即跳转到该分段起始点</param>
        public void SyncSection(int sectionIndex, float targetDuration, bool jumpImmediately = false)
        {
            if (_coordinator != null)
            {
                _coordinator.SyncSection(_layerIndex, _slotIndex, _generation, sectionIndex, targetDuration, jumpImmediately);
            }
        }

        /// <summary>
        /// 清除指定分段的自定义速率配置，使其恢复为资产基础速率。
        /// </summary>
        /// <param name="sectionIndex">物理分段索引</param>
        public void ClearSectionSync(int sectionIndex)
        {
            if (_coordinator != null)
            {
                _coordinator.ClearSectionSync(_layerIndex, _slotIndex, _generation, sectionIndex);
            }
        }

        /// <summary>
        /// 设置当前播放速率倍率。
        /// </summary>
        /// <param name="rate">速率倍率</param>
        public void SetPlaybackRate(float rate)
        {
            if (_coordinator != null)
            {
                _coordinator.SetPlaybackRate(_layerIndex, _slotIndex, _generation, rate);
            }
        }

        /// <summary>
        /// 暂停或恢复播放。
        /// </summary>
        /// <param name="isPaused">是否暂停</param>
        public void SetPaused(bool isPaused)
        {
            if (_coordinator != null)
            {
                _coordinator.SetPaused(_layerIndex, _slotIndex, _generation, isPaused);
            }
        }

        /// <summary>
        /// 外部主动打断停止该蒙太奇播放，进入淡出阶段并安全退出所有活跃动作块。
        /// </summary>
        /// <param name="blendOutTime">自定义淡出时长（若为空使用资产默认配置）</param>
        public void Stop(float? blendOutTime = null)
        {
            if (_coordinator != null)
            {
                _coordinator.StopHandle(_layerIndex, _slotIndex, _generation, blendOutTime);
            }
        }

        /// <summary>
        /// 获取指定物理分段的原始物理持续时间（秒）。
        /// </summary>
        /// <param name="sectionIndex">物理分段索引</param>
        /// <returns>分段持续秒数</returns>
        public float GetSectionDuration(int sectionIndex)
        {
            return SourceAsset != null ? SourceAsset.GetSectionDuration(sectionIndex) : 0f;
        }

        /// <summary>
        /// 获取指定物理分段在时间轴上的起止时间区间 [start, end]。
        /// </summary>
        /// <param name="sectionIndex">物理分段索引</param>
        /// <returns>包含 start 与 end 的时间元组</returns>
        public (float start, float end) GetSectionRange(int sectionIndex)
        {
            return SourceAsset != null ? SourceAsset.GetSectionRange(sectionIndex) : (0f, 0f);
        }

        /// <summary>
        /// 获取当前播放进度在指定物理分段中的归一化局部进度 [0.0, 1.0]。
        /// </summary>
        /// <param name="sectionIndex">物理分段索引</param>
        /// <returns>分段内部归一化进度</returns>
        public float GetSectionProgress(int sectionIndex)
        {
            if (SourceAsset == null) return 0f;
            var (start, end) = SourceAsset.GetSectionRange(sectionIndex);
            float len = Mathf.Max(0.0001f, end - start);
            return Mathf.Clamp01((ElapsedTime - start) / len);
        }

        #endregion

        #region 相等性与运算符重载

        public bool Equals(MontageHandle other) =>
            _coordinator == other._coordinator &&
            _layerIndex == other._layerIndex &&
            _slotIndex == other._slotIndex &&
            _generation == other._generation;

        public override bool Equals(object obj) => obj is MontageHandle other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(_coordinator, _layerIndex, _slotIndex, _generation);

        public static bool operator ==(MontageHandle left, MontageHandle right) => left.Equals(right);

        public static bool operator !=(MontageHandle left, MontageHandle right) => !left.Equals(right);

        public override string ToString() => $"MontageHandle(Layer: {_layerIndex}, Slot: {_slotIndex}, Gen: {_generation}, Valid: {IsValid})";

        #endregion
    }
}
