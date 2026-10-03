using System;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 蒙太奇动画器中介派发组件。
    /// 自动挂载在包含 Animator 组件的 GameObject（可位于角色子层级），
    /// 实现 OnAnimatorMove 回调以精准拦截 Unity 引擎底层默认的 Transform 位移累加，
    /// 并将根运动增量安全传递给父级的 MontageCoordinator 进行解耦分发。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public class MontageAnimatorDispatcher : MonoBehaviour
    {
        #region 保护与私有字段

        protected Animator _animator;
        protected Action<Vector3, Quaternion> _onAnimatorMoveCallback;

        #endregion

        #region Unity 生命周期

        protected virtual void Awake()
        {
            if (_animator == null)
            {
                _animator = GetComponent<Animator>();
            }
        }

        protected virtual void OnAnimatorMove()
        {
            if (_animator == null)
            {
                _animator = GetComponent<Animator>();
                if (_animator == null) return;
            }

            _onAnimatorMoveCallback?.Invoke(_animator.deltaPosition, _animator.deltaRotation);
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 绑定来自 MontageCoordinator 的根运动处理委托。
        /// </summary>
        /// <param name="callback">接收 (deltaPosition, deltaRotation) 的回调方法</param>
        public virtual void Bind(Action<Vector3, Quaternion> callback)
        {
            if (_animator == null)
            {
                _animator = GetComponent<Animator>();
            }
            _onAnimatorMoveCallback = callback;
        }

        /// <summary>
        /// 解绑根运动处理委托。
        /// </summary>
        public virtual void Unbind()
        {
            _onAnimatorMoveCallback = null;
        }

        #endregion
    }
}
