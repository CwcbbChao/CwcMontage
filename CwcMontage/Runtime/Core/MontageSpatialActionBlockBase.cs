using System;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// Spatial attachment and position synchronization modes.
    /// </summary>
    public enum MontageAttachMode
    {
        /// <summary>
        /// Continuously follows target bone world position and rotation.
        /// </summary>
        FollowTarget = 0,

        /// <summary>
        /// Follows world position only; world rotation remains fixed.
        /// </summary>
        FollowPositionOnly = 1,

        /// <summary>
        /// Samples world transform once at start and stays fixed in world space.
        /// </summary>
        WorldPositionAtStart = 2
    }

    /// <summary>
    /// Base class for action blocks with spatial attachment and bone tracking capabilities.
    /// </summary>
    [Serializable]
    public abstract class MontageSpatialActionBlockBase : MontageActionBlockBase
    {
        #region Serialized Fields

        [Header("Attachment & Transform")]
        [Tooltip("Target bone for spatial attachment.")]
        [SerializeField] private MontageTargetBone _targetBone = MontageTargetBone.Root;

        [Tooltip("Attachment and tracking mode.")]
        [SerializeField] private MontageAttachMode _attachMode = MontageAttachMode.FollowTarget;

        [Tooltip("Local position offset relative to target bone.")]
        [SerializeField] private Vector3 _positionOffset = Vector3.zero;

        [Tooltip("Local rotation offset in Euler angles relative to target bone.")]
        [SerializeField] private Vector3 _rotationOffset = Vector3.zero;

        [Tooltip("Local scale multiplier.")]
        [SerializeField] private Vector3 _scale = Vector3.one;

        #endregion

        #region Protected & Private Runtime Fields

        [NonSerialized] protected Transform _cachedTargetBone;

        #endregion

        #region Properties

        public MontageTargetBone TargetBone => _targetBone;
        public MontageAttachMode AttachMode => _attachMode;
        public Vector3 PositionOffset => _positionOffset;
        public Vector3 RotationOffset => _rotationOffset;
        public Vector3 Scale => _scale;

        #endregion

        #region Spatial Transform & Lifecycle Helpers

        /// <summary>
        /// Resolves and caches the target attachment bone Transform on OnEnter (zero GC allocation).
        /// </summary>
        protected virtual void ResolveTargetBone(in MontageActionContext context)
        {
            _cachedTargetBone = context.GetTargetBone(_targetBone);
        }

        /// <summary>
        /// Calculates the target world position and world rotation from the current attachment bone and the configured offsets.
        /// </summary>
        /// <param name="worldPos">Calculated world position</param>
        /// <param name="worldRot">Calculated world rotation</param>
        protected void CalculateWorldTransform(out Vector3 worldPos, out Quaternion worldRot)
        {
            if (_cachedTargetBone != null)
            {
                worldPos = _cachedTargetBone.TransformPoint(_positionOffset);
                worldRot = _cachedTargetBone.rotation * Quaternion.Euler(_rotationOffset);
            }
            else
            {
                worldPos = _positionOffset;
                worldRot = Quaternion.Euler(_rotationOffset);
            }
        }

        /// <summary>
        /// Calculates the target parent Transform to use when spawning.
        /// Never attaches instances as children of character bones, fully avoiding character local-scale distortion.
        /// </summary>
        protected Transform GetSpawnParent(in MontageActionContext context, bool isPreview = false)
        {
            // In preview, parent under the viewport host root for isolation; at runtime stays null (parented under the persistent object-pool root)
            return isPreview && context.TargetObject != null ? context.TargetObject.transform : null;
        }

        /// <summary>
        /// Called from OnUpdate or OnPreviewUpdate to drive the instance's world-space position and rotation according to AttachMode.
        /// </summary>
        /// <param name="instance">Spawned GameObject instance</param>
        /// <param name="context">Current animation context</param>
        protected void UpdateSpatialTransform(GameObject instance, in MontageActionContext context)
        {
            if (instance == null || _attachMode == MontageAttachMode.WorldPositionAtStart)
            {
                return;
            }

            if (_cachedTargetBone == null)
            {
                ResolveTargetBone(context);
                if (_cachedTargetBone == null)
                {
                    return;
                }
            }

            Vector3 currentWorldPos = _cachedTargetBone.TransformPoint(_positionOffset);

            if (_attachMode == MontageAttachMode.FollowTarget)
            {
                Quaternion currentWorldRot = _cachedTargetBone.rotation * Quaternion.Euler(_rotationOffset);
                instance.transform.SetPositionAndRotation(currentWorldPos, currentWorldRot);
            }
            else if (_attachMode == MontageAttachMode.FollowPositionOnly)
            {
                instance.transform.position = currentWorldPos;
            }
        }

        public override void OnExit(in MontageActionContext context)
        {
            base.OnExit(context);
            _cachedTargetBone = null;
        }

        public override void OnPreviewExit(in MontageActionContext context)
        {
            base.OnPreviewExit(context);
            _cachedTargetBone = null;
        }

        /// <summary>
        /// Immediately synchronizes and updates the preview object's Transform in place when spatial transform properties (position offset, rotation offset, scale, or bone) are modified externally in the editor viewport preview, without destroying and rebuilding it.
        /// </summary>
        /// <param name="instance">Spawned GameObject instance</param>
        /// <param name="context">Current animation context</param>
        public virtual void UpdatePreviewTransform(GameObject instance, in MontageActionContext context)
        {
            if (instance == null) return;

            ResolveTargetBone(context);
            CalculateWorldTransform(out Vector3 worldPos, out Quaternion worldRot);

            if (_attachMode == MontageAttachMode.FollowPositionOnly)
            {
                instance.transform.position = worldPos;
            }
            else
            {
                instance.transform.SetPositionAndRotation(worldPos, worldRot);
            }
        }

        #endregion
    }

    /// <summary>
    /// Strongly typed abstract base class for action blocks with spatial attachment and position synchronization.
    /// All runtime state is delegated to TState (derived from MontageSpatialBlockState), keeping action block assets 100% read-only and 0-GC.
    /// </summary>
    /// <typeparam name="TState">State container derived from MontageSpatialBlockState</typeparam>
    [Serializable]
    public abstract class MontageSpatialActionBlockBase<TState> : MontageActionBlockBase<TState>
        where TState : MontageSpatialBlockState, new()
    {
        #region Serialized Fields

        [Header("Attachment & Transform")]
        [Tooltip("Target attachment bone (supports the 8 common humanoid core bones plus Root).")]
        [SerializeField] private MontageTargetBone _targetBone = MontageTargetBone.Root;

        [Tooltip("Attachment and position synchronization mode.")]
        [SerializeField] private MontageAttachMode _attachMode = MontageAttachMode.FollowTarget;

        [Tooltip("Local position offset relative to the target attachment bone.")]
        [SerializeField] private Vector3 _positionOffset = Vector3.zero;

        [Tooltip("Local rotation offset relative to the target attachment bone (Euler angles).")]
        [SerializeField] private Vector3 _rotationOffset = Vector3.zero;

        [Tooltip("Local scale multiplier relative to the prefab.")]
        [SerializeField] private Vector3 _scale = Vector3.one;

        #endregion

        #region Properties

        public MontageTargetBone TargetBone => _targetBone;
        public MontageAttachMode AttachMode => _attachMode;
        public Vector3 PositionOffset => _positionOffset;
        public Vector3 RotationOffset => _rotationOffset;
        public Vector3 Scale => _scale;

        #endregion

        #region Protected Spatial Transform & Lifecycle Helpers

        /// <summary>
        /// Resolves and caches the target attachment bone Transform into state (zero GC allocation).
        /// </summary>
        protected virtual void ResolveTargetBone(in MontageActionContext context, TState state)
        {
            if (state != null)
            {
                state.CachedTargetBone = context.GetTargetBone(_targetBone);
            }
        }

        /// <summary>
        /// Calculates the target world position and world rotation from the current attachment bone and the configured offsets.
        /// </summary>
        protected void CalculateWorldTransform(TState state, out Vector3 worldPos, out Quaternion worldRot)
        {
            if (state != null && state.CachedTargetBone != null)
            {
                worldPos = state.CachedTargetBone.TransformPoint(_positionOffset);
                worldRot = state.CachedTargetBone.rotation * Quaternion.Euler(_rotationOffset);
            }
            else
            {
                worldPos = _positionOffset;
                worldRot = Quaternion.Euler(_rotationOffset);
            }
        }

        /// <summary>
        /// Calculates the target parent Transform to use when spawning.
        /// Never attaches instances as children of character bones, fully avoiding character local-scale distortion.
        /// </summary>
        protected Transform GetSpawnParent(in MontageActionContext context, bool isPreview = false)
        {
            return isPreview && context.TargetObject != null ? context.TargetObject.transform : null;
        }

        /// <summary>
        /// Drives the instance's world-space position and rotation according to AttachMode.
        /// </summary>
        protected void UpdateSpatialTransform(GameObject instance, in MontageActionContext context, TState state)
        {
            if (instance == null || _attachMode == MontageAttachMode.WorldPositionAtStart)
            {
                return;
            }

            if (state == null)
            {
                return;
            }

            if (state.CachedTargetBone == null)
            {
                ResolveTargetBone(context, state);
                if (state.CachedTargetBone == null)
                {
                    return;
                }
            }

            Vector3 currentWorldPos = state.CachedTargetBone.TransformPoint(_positionOffset);

            if (_attachMode == MontageAttachMode.FollowTarget)
            {
                Quaternion currentWorldRot = state.CachedTargetBone.rotation * Quaternion.Euler(_rotationOffset);
                instance.transform.SetPositionAndRotation(currentWorldPos, currentWorldRot);
            }
            else if (_attachMode == MontageAttachMode.FollowPositionOnly)
            {
                instance.transform.position = currentWorldPos;
            }
        }

        #endregion

        #region Editor Preview Helper Methods

        /// <summary>
        /// Immediately synchronizes and updates the preview object's Transform in place in the editor viewport preview, without destroying and rebuilding it.
        /// </summary>
        public virtual void UpdatePreviewTransform(GameObject instance, in MontageActionContext context)
        {
            if (instance == null) return;

            var state = GetEditorPreviewState();
            if (state == null) return;

            ResolveTargetBone(context, state);
            CalculateWorldTransform(state, out Vector3 worldPos, out Quaternion worldRot);

            if (_attachMode == MontageAttachMode.FollowPositionOnly)
            {
                instance.transform.position = worldPos;
            }
            else
            {
                instance.transform.SetPositionAndRotation(worldPos, worldRot);
            }
        }

        #endregion
    }
}
