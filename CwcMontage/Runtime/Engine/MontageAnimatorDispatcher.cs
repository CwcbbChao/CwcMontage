using System;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// Mediator dispatch component for the montage animator.
    /// Automatically attached to any GameObject containing an Animator component (may be at a child level of the character);
    /// implements the OnAnimatorMove callback to precisely intercept the Transform displacement accumulation applied by default in the Unity engine,
    /// and safely forwards root motion deltas to the parent MontageController for decoupled dispatch.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public class MontageAnimatorDispatcher : MonoBehaviour
    {
        #region Protected and Private Fields

        protected Animator _animator;
        protected Action<Vector3, Quaternion> _onAnimatorMoveCallback;

        #endregion

        #region Unity Lifecycle

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

        #region Public Methods

        /// <summary>
        /// Binds the root motion handling delegate from MontageController.
        /// </summary>
        /// <param name="callback">Callback method receiving (deltaPosition, deltaRotation)</param>
        public virtual void Bind(Action<Vector3, Quaternion> callback)
        {
            if (_animator == null)
            {
                _animator = GetComponent<Animator>();
            }
            _onAnimatorMoveCallback = callback;
        }

        /// <summary>
        /// Unbinds the root motion handling delegate.
        /// </summary>
        public virtual void Unbind()
        {
            _onAnimatorMoveCallback = null;
        }

        #endregion
    }
}
