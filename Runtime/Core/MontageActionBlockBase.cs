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

        #endregion

        #region 公共方法 (生命周期钩子)

        /// <summary>
        /// 校验当前动作块是否满足进入执行的先决条件。
        /// </summary>
        /// <param name="context">当前动画帧执行上下文</param>
        /// <returns>若返回 false 则跳过本次触发</returns>
        public virtual bool CanEnter(in MontageActionContext context)
        {
            return _isEnabled;
        }

        /// <summary>
        /// 当时间轴首次进入动作块时间区间时触发。
        /// </summary>
        /// <param name="context">当前动画帧执行上下文</param>
        public virtual void OnEnter(in MontageActionContext context)
        {
            _isActive = true;
        }

        /// <summary>
        /// 在动作块有效时间区间内每帧持续更新。
        /// </summary>
        /// <param name="context">当前动画帧执行上下文</param>
        /// <param name="deltaTime">自上一帧经过的有效时间步长</param>
        public virtual void OnUpdate(in MontageActionContext context, float deltaTime)
        {
        }

        /// <summary>
        /// 当时间轴离开动作块时间区间、或动画被外部打断/跳转退出时触发。
        /// </summary>
        /// <param name="context">当前动画帧执行上下文</param>
        public virtual void OnExit(in MontageActionContext context)
        {
            _isActive = false;
        }

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
