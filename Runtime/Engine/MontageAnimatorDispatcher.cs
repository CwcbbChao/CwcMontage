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
        #region 私有字段

        private Action<Vector3, Quaternion> _onAnimatorMoveCallback;

        #endregion

        #region Unity 生命周期

        private void OnAnimatorMove()
        {
            var animator = GetComponent<Animator>();
            if (animator == null) return;

            _onAnimatorMoveCallback?.Invoke(animator.deltaPosition, animator.deltaRotation);
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 绑定来自 MontageCoordinator 的根运动处理委托。
        /// </summary>
        /// <param name="callback">接收 (deltaPosition, deltaRotation) 的回调方法</param>
        public void Bind(Action<Vector3, Quaternion> callback)
        {
            _onAnimatorMoveCallback = callback;
        }

        /// <summary>
        /// 解绑根运动处理委托。
        /// </summary>
        public void Unbind()
        {
            _onAnimatorMoveCallback = null;
        }

        #endregion
    }
}
