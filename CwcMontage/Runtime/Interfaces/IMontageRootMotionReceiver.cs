using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// Receiver interface for filtered Root Motion delta updates from the montage system.
    /// Character movement controllers can implement this to receive planar/vertical displacements and rotation.
    /// </summary>
    public interface IMontageRootMotionReceiver
    {
        /// <summary>
        /// Called when the montage dispatcher computes valid Root Motion position displacement for the current frame.
        /// </summary>
        /// <param name="deltaPosition">Displacement vector for this frame.</param>
        void OnMontageRootMotionDisplacement(Vector3 deltaPosition);

        /// <summary>
        /// Called when the montage dispatcher computes valid Root Motion rotation delta for the current frame.
        /// </summary>
        /// <param name="deltaRotation">Rotation delta quaternion for this frame.</param>
        void OnMontageRootMotionRotation(Quaternion deltaRotation);
    }
}
