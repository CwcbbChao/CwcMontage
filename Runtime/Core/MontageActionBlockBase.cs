using System;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 蒙太奇动作块抽象基类。
    /// 所有自定义视听表现（如播放特效、音效、顿帧、材质高亮等）均继承自此类。
    /// 核心系统仅依赖此抽象基类，不感知任何具体表现实现（符合开闭原则 OCP）。
    /// </summary>
    [Serializable]
    public abstract class MontageActionBlockBase
    {
        #region Inspector 字段

        [Tooltip("自定义动作块备注名称。若为空则在编辑器中默认显示类名或特性名称。")]
        [SerializeField] private string _customName;

        [Tooltip("是否启用该动作块。禁用后在运行时和预览中均不会被触发。")]
        [SerializeField] private bool _isEnabled = true;

        #endregion

        #region 私有字段

        [NonSerialized] private bool _isActive;
        [NonSerialized] private float _blockDuration = 1.0f;

        #endregion

        #region 公共属性

        /// <summary>
        /// 自定义动作块名称。
        /// </summary>
        public string CustomName => _customName;

        /// <summary>
        /// 是否已启用（true 为启用，false 为禁用）。
        /// </summary>
        public bool IsEnabled => _isEnabled;

        /// <summary>
        /// 是否被禁用（兼容属性）。
        /// </summary>
        public bool IsDisabled => !_isEnabled;

        /// <summary>
        /// 当前动作块是否处于活跃执行状态。
        /// </summary>
        public bool IsActive => _isActive;

        /// <summary>
        /// 当前动作块在时间轴上的持续时长（秒）。由包含它的 MontageActionBlockData 或调度系统注入。
        /// </summary>
        public float BlockDuration
        {
            get => _blockDuration;
            set => _blockDuration = Mathf.Max(0.0001f, value);
        }

        /// <summary>
        /// 该动作块是否支持媒体片段截取（Trimming）与随 Block 缩放（Time-Stretching）。
        /// 默认为 false。媒体型动作块（如 VFX、Audio、Video 等）可重写返回 true。
        /// </summary>
        public virtual bool IsTrimmableClip => false;

        /// <summary>
        /// 媒体片段截取的起始时间（秒）。
        /// </summary>
        public virtual float ClipStartTime
        {
            get => 0f;
            set { }
        }

        /// <summary>
        /// 媒体片段截取的结束时间（秒）。
        /// </summary>
        public virtual float ClipEndTime
        {
            get => BlockDuration;
            set { }
        }

        /// <summary>
        /// 媒体截取的有效片段时长（秒）。即 ClipEndTime - ClipStartTime。
        /// </summary>
        public virtual float EffectiveClipDuration => Mathf.Max(0.001f, ClipEndTime - ClipStartTime);

        /// <summary>
        /// 随 Block 长度自适应拉伸后的播放速度倍率。
        /// 当 Block 长度等于有效截取时长时为 1.0x 原速；
        /// 当 Block 缩短为一半时为 2.0x 加速；当 Block 拉长为两倍时为 0.5x 减速。
        /// </summary>
        public virtual float SpeedMultiplier => EffectiveClipDuration / Mathf.Max(0.001f, BlockDuration);

        #endregion

        #region 公共方法 (剪辑与时长适配)

        /// <summary>
        /// 将截取区间自适应匹配为目标时长（如当前 Block 时长）。
        /// </summary>
        /// <param name="targetDuration">目标时长（秒）</param>
        public virtual void FitClipToDuration(float targetDuration)
        {
            ClipEndTime = ClipStartTime + Mathf.Max(0.001f, targetDuration);
        }

        #endregion

        #region 公共方法 (运行时生命周期钩子)

        /// <summary>
        /// 校验当前动作块是否满足进入执行的先决条件（运行时专用）。
        /// </summary>
        /// <param name="context">当前动画帧执行上下文</param>
        /// <returns>若返回 false 则跳过本次触发</returns>
        public virtual bool CanEnter(in MontageActionContext context)
        {
            return _isEnabled;
        }

        /// <summary>
        /// 当时间轴首次进入动作块时间区间时触发（运行时专用）。
        /// </summary>
        /// <param name="context">当前动画帧执行上下文</param>
        public virtual void OnEnter(in MontageActionContext context)
        {
            _isActive = true;
        }

        /// <summary>
        /// 在动作块有效时间区间内每帧持续更新（运行时专用）。
        /// </summary>
        /// <param name="context">当前动画帧执行上下文</param>
        /// <param name="deltaTime">自上一帧经过的有效时间步长</param>
        public virtual void OnUpdate(in MontageActionContext context, float deltaTime)
        {
        }

        /// <summary>
        /// 当时间轴离开动作块时间区间、或动画被外部打断/跳转退出时触发（运行时专用）。
        /// </summary>
        /// <param name="context">当前动画帧执行上下文</param>
        public virtual void OnExit(in MontageActionContext context)
        {
            _isActive = false;
        }

        #endregion

        #region 公共方法 (编辑器视口预览生命周期钩子)

        /// <summary>
        /// 校验当前动作块是否满足进入视口预览的先决条件（编辑器预览专用）。
        /// </summary>
        /// <param name="context">当前动画帧执行上下文</param>
        /// <returns>若返回 false 则跳过本次预览触发</returns>
        public virtual bool CanPreviewEnter(in MontageActionContext context)
        {
            return _isEnabled;
        }

        /// <summary>
        /// 当编辑器视口时间轴首次进入动作块时间区间时触发（编辑器预览专用，默认空实现）。
        /// </summary>
        /// <param name="context">当前动画帧执行上下文</param>
        public virtual void OnPreviewEnter(in MontageActionContext context)
        {
        }

        /// <summary>
        /// 在编辑器视口动作块有效时间区间内每帧持续更新（编辑器预览专用，默认空实现，用于粒子 Simulate 步进等）。
        /// </summary>
        /// <param name="context">当前动画帧执行上下文</param>
        /// <param name="deltaTime">自上一帧经过的有效时间步长</param>
        public virtual void OnPreviewUpdate(in MontageActionContext context, float deltaTime)
        {
        }

        /// <summary>
        /// 当编辑器视口时间轴离开动作块时间区间、停止或重置时触发（编辑器预览专用，默认空实现，用于清理视口临时实例）。
        /// </summary>
        /// <param name="context">当前动画帧执行上下文</param>
        public virtual void OnPreviewExit(in MontageActionContext context)
        {
        }

        /// <summary>
        /// 在编辑器非播放状态下拖拽时间轴（Scrubbing）或单帧跳转时触发，将视口预览状态精准对齐到当前动作块局部时间（秒）。
        /// </summary>
        /// <param name="context">当前动画帧执行上下文</param>
        /// <param name="localTime">当前时间相对于该动作块开始时间的局部秒数</param>
        public virtual void OnPreviewScrub(in MontageActionContext context, float localTime)
        {
        }

        /// <summary>
        /// 当该动作块在 Inspector 中的参数发生修改时触发，允许当前已实例化的预览对象原地更新（如同步 Transform），避免销毁重建。
        /// </summary>
        /// <param name="context">当前动画帧执行上下文</param>
        public virtual void OnPreviewParametersChanged(in MontageActionContext context)
        {
        }

        /// <summary>
        /// 当在编辑器中外部修改了动作块参数时，由动作块自身多态判断是否需要强制销毁重建视口预览实例（例如更换了核心资源资产）。
        /// 默认返回 false（即支持原地平滑更新）；派生类若更换了核心资源引用可重写返回 true。
        /// </summary>
        /// <param name="newBlock">包含最新参数的动作块源数据</param>
        /// <returns>若返回 true 则主体框架将调用 OnPreviewExit 销毁旧实例并重新实例化，否则原地更新</returns>
        public virtual bool RequiresPreviewRecreate(MontageActionBlockBase newBlock)
        {
            return false;
        }

        /// <summary>
        /// 动作块向 Inspector Timing 面板提供的附加描述信息（如特效自然时长、自适应缩放倍率等）。默认返回 null。
        /// </summary>
        public virtual string GetTimingCustomHint()
        {
            return null;
        }

        #endregion

        #region 公共方法 (克隆与实例化)

        /// <summary>
        /// 创建该动作块实例的独立深拷贝副本，供运行时独立播放使用。
        /// </summary>
        /// <returns>克隆后的动作块实例</returns>
        public virtual MontageActionBlockBase Clone()
        {
            return (MontageActionBlockBase)MemberwiseClone();
        }

        #endregion
    }
}
