using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// Runtime state interface for montage action blocks.
    /// Implemented as reference types to allow zero-allocation pooling without boxing.
    /// </summary>
    public interface IMontageBlockState
    {
        /// <summary>
        /// Resets all mutable runtime fields when leaving the block range, interrupting, or recycling.
        /// </summary>
        void Reset();
    }

    /// <summary>
    /// Base runtime state for action blocks that track spatial transforms or character bones.
    /// </summary>
    public class MontageSpatialBlockState : IMontageBlockState
    {
        #region Public Fields

        public Transform CachedTargetBone;

        #endregion

        #region Public Methods

        public virtual void Reset()
        {
            CachedTargetBone = null;
        }

        #endregion
    }
}
